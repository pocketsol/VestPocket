using System.Text;

namespace VestPocket;

/// <summary>
/// A typed Radix Tree Node containing a payload value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity stored within this node</typeparam>
internal class Node<T> where T : class, IEntity
{
    public string KeySegment;
    public Node<T>[] Children = Empty;
    public static readonly Node<T>[] Empty = Array.Empty<Node<T>>();
    public T Value;
    public char FirstChar;

    public Node()
    {
        this.KeySegment = String.Empty;
    }

    public Node(string label)
    {
        this.KeySegment = label;
        this.FirstChar = KeySegment[0];
    }

    public void SetValue(ref Node<T> rootNode, in ReadOnlySpan<char> key, T value)
    {
        var searchNode = rootNode;
        var searchKey = key;
    FakeTailRecursion:
        // Set on a child
        var searchFirstChar = searchKey[0];
        Node<T> matchingChild = null;
        int childIndex = 0;
        for (int i = 0; i < searchNode.Children.Length; i++)
        {
            Node<T> child = searchNode.Children[i];

            if (child.FirstChar == searchFirstChar)
            {
                childIndex = i;
                matchingChild = child;
                break;
            }
            if (child.FirstChar > searchFirstChar)
            {
                break;
            }
            childIndex = i + 1;
        }

        //var matchingChild = searchNode.FindMatchingChild(searchKey, out var matchingLength, out var matchingIndex);
        if (matchingChild is not null)
        {
            var matchingLength = searchKey.CommonPrefixLength(matchingChild.KeySegment);
            if (matchingLength == searchKey.Length)
            {
                if (matchingLength == matchingChild.KeySegment.Length)
                {
                    // We found a child node that matches our key exactly
                    // E.g. Key = "apple" and child key = "apple"
                    matchingChild.Value = value;
                }
                else
                {

                    // https://en.wikipedia.org/wiki/Radix_tree#/media/File:Inserting_the_word_'team'_into_a_Patricia_trie_with_a_split.png

                    // We matched the whole set key, but not the entire child key. We need to split the child key
                    //matchingChild.SplitKeySegmentAtLength(matchingLength);
                    searchNode.SplitChild(matchingLength, childIndex);
                    matchingChild = searchNode.Children[childIndex];
                    matchingChild.Value = value;
                }

            }
            else
            {
                // We matched part of the set key on a child
                if (matchingLength == matchingChild.KeySegment.Length)
                {
                    // and the entire child key
                    searchKey = searchKey.Slice(matchingLength);
                    searchNode = matchingChild;
                    goto FakeTailRecursion;
                }
                else
                {
                    // and only part of the child key
                    searchNode.SplitChild(matchingLength, childIndex);
                    matchingChild = searchNode.Children[childIndex];
                    searchKey = searchKey.Slice(matchingLength);
                    searchNode = matchingChild;
                    goto FakeTailRecursion;

                }
            }
        }
        else
        {
            // There were no matching children. 
            // E.g. Key = "apple" and no child that even starts with 'a'. Add a new child node
            string keySegment = new string(searchKey);
            var newChild = new Node<T>(keySegment);
            newChild.Value = value;
            searchNode.AddChild(ref rootNode, newChild, childIndex);
        }
    }

    public Node<T> Clone()
    {
        return new Node<T>(KeySegment)
        {
            Children = Children,
            Parent = Parent,
            ParentIndex = ParentIndex,
            Value = Value
        };
    }

    public Node<T> NextSibling => Parent.Children.Length > ParentIndex + 1 ? Parent.Children[ParentIndex + 1] : null;
    public Node<T> FirstChild => Children.Length > 0 ? Children[0] : null;
    public Node<T> Back => Parent.KeySegment.Length > 0 ? Parent : null;
    public Node<T> Next => FirstChild ?? NextSibling ?? Back;

    private void AddChild(ref Node<T> rootNode, Node<T> newChild, int afterIndex)
    {
        if (Children.Length == 0)
        {
            newChild.ParentIndex = 0;
            newChild.Parent = this;
            Children = new Node<T>[1] { newChild };
        }
        else
        {
            // This operation is messy.
            // We are inserting the new child into the correct place for it
            // to be sorted in the array, but we need to copy and replace
            // any nodes that require patches to their Paret/ParentIndex values
            // in a concurrent safe way.

            // Right now this patches the references on clones of the children
            // onto a clone of 'this' then replaces the node reference in its 
            // parent with the clone of this (effectively replacing the current
            // node in the graph with a patched clone containing the changes).

            // Being done as a single assignment with a single writer allows this
            // to work and pass concurrency checks.
            var newSelf = this.KeySegment == string.Empty ? this : this.Clone();
            newChild.Parent = newSelf;
            var newLength = newSelf.Children.Length + 1;
            var newChildArray = new Node<T>[newLength];

            // Poor man's sorted insert
            // We could probably greatly reduce the performance implications
            // here by not doing a sorted insert and copy of the current node
            // but for larger child collections (utilizing UTF8 keys for example)
            // being able to use binary searches on sorted children should offer
            // improved performance
            for(int i = 0; i < newChildArray.Length; i++)
            {
                if (i == afterIndex)
                {
                    newChildArray[i] = newChild;
                }
                else
                {
                    int offset = i > afterIndex ? -1 : 0;
                    newChildArray[i] = newSelf.Children[i + offset].Clone();
                    newChildArray[i].ParentIndex = i;
                    foreach (var clonedChild in newChildArray[i].Children)
                    {
                        clonedChild.Parent = newChildArray[i];
                    }
                }
            }

            for(int i = 0; i < newChildArray.Length; i++)
            {
                var child = newChildArray[i];
                child.ParentIndex = i;
                child.Parent = newSelf;
            }

            newSelf.Children = newChildArray;

            if (this != rootNode)
            {
                newSelf.Parent.Children[newSelf.ParentIndex] = newSelf;
            }
            else
            {
                rootNode = newSelf;
            }

        }
    }

    private void SplitChild(int startingCharacter, int childIndex)
    {
        var child = Children[childIndex];
        // We are taking a child node and splitting it at a specific number of
        // characters in its key segment

        // E.g.
        // We have nodes A => BC => etc...
        // and we want A => B => C => etc..
        // A is the current 'this' node of this method
        // BC is the original child node
        // B is the splitParent, and gets a null value
        // C is the new splitChild, it retains the original value and children of the 'BC' node

        var splitChildKey = child.KeySegment.Substring(startingCharacter);
        var splitChild = new Node<T>(splitChildKey) { Value = child.Value};
        var splitChildChildren = new Node<T>[child.Children.Length];
        for(int i = 0; i < child.Children.Length; i++)
        {
            var splitChildChild = child.Children[i].Clone();
            splitChildChildren[i] = splitChildChild;
            splitChildChild.ParentIndex = i;
            splitChildChild.Parent = splitChild;
        }

        var splitParentKey = child.KeySegment.Substring(0, startingCharacter);
        var splitParent = new Node<T>(splitParentKey) { Children = new Node<T>[] { splitChild } };

        splitParent.Parent = this;
        splitParent.ParentIndex = childIndex;

        splitChild.Parent = splitParent;
        splitChild.ParentIndex = 0;

        // The order we perform the above operations is very important for concurrency reasons

        Children[childIndex] = splitParent;
    }

    private Node<T> Parent;
    private int ParentIndex;

    public Node<T> GetValue(in ReadOnlySpan<char> key)
    {
        var searchNode = this;
        var searchKey = key;
        char searchFirstChar;

        FakeTailRecursion:
        var searchChildren = searchNode.Children;
        searchFirstChar = searchKey[0];

        for (int i = 0; i < searchChildren.Length; i++)
        {
            Node<T> child = searchChildren[i];
            if (searchFirstChar != child.FirstChar) continue;

            var matchingBytes = searchKey.CommonPrefixLength(child.KeySegment);
            //var matchingBytes = GetMatchingBytes(key, child.KeySegment);

            if (matchingBytes == searchKey.Length)
            {
                if (matchingBytes == child.KeySegment.Length)
                {
                    // We found a key with an exact match
                    return child;
                }
                else
                {
                    // We found a key that was longer than the
                    // one we were looking for that matched the length of the key

                    // In a radix tree, that means our key wasn't found, because if it
                    // existed, it would have been split at our length
                    return null;
                }
            }
            else if (matchingBytes < searchKey.Length)
            {
                searchKey = searchKey.Slice(matchingBytes);
                searchNode = child;
                goto FakeTailRecursion; // C# maintainers, please add tail call optimizations
            }
        }
        return null;

    }

    /// <summary>
    /// Creates a representation of the node and its parent key segments connected by delimiters.
    /// Example: ROOT > A > pp > l > e
    /// </summary>
    public string ToGraphNodeString()
    {
        var node = this;
        Stack<Node<T>> stack = new();
        stack.Push(node);
        StringBuilder sb = new();
        var parent = node.Parent;
        while (parent != null)
        {
            stack.Push(parent);
            parent = parent.Parent;
        }
        while(stack.TryPop(out var poppedNode))
        {
            if (poppedNode.KeySegment == string.Empty)
            {
                sb.Append("ROOT");
            }
            else
            {
                sb.Append(" > ");
                sb.Append(poppedNode.KeySegment);
            }
        }
        return sb.ToString();
    }

    public IEnumerable<TSelection> Collect<TSelection>() where TSelection : class, T
    {
        if (Value is not null)
        {
            yield return (TSelection)Value;
        }
        if (Children.Length == 0)
        {
            yield break;
        }

        // We are doing a depth first search for all the values to collect
        // and using parent references and parent indexes (the index of the 
        // iterator node within it's parent) to backtrack and find sibblings
        // without having to allocate additional storage (such as using a stack)
        var searchNode = Children[0];

        // Due to the way that iterators work and the concurrency model for this
        // graph, sometimes 'this' reference in this method is no longer in the 
        // reference version of the radix tree. To understand when we have completed
        // our search, we need to know when we are back at the depth we started at.
        int depthFromRoot = 1;

        while (true)
        {

            if (searchNode.Value is not null)
            {
                yield return (TSelection)searchNode.Value;
            }
            if (searchNode.Children.Length != 0)
            {
                // Dig depth first
                searchNode = searchNode.Children[0];
                depthFromRoot++;
                continue;
            }
            else
            {
                var nextSibbling = searchNode.NextSibling;
                if (nextSibbling is not null)
                {
                    searchNode = nextSibbling;
                }
                else
                {
                    while(true)
                    {
                        depthFromRoot--;
                        if (depthFromRoot == 0)
                        {
                            yield break;
                        }
                        var nextParentSibbling = searchNode.Parent.NextSibling;
                        if (nextParentSibbling is null)
                        {
                            searchNode = searchNode.Parent;
                        }
                        else
                        {
                            searchNode = nextParentSibbling;
                            break;
                        }
                    }

                }

            }

        }

    }
    
    public IEnumerable<TSelection> EnumeratePrefix<TSelection>(ReadOnlySpan<char> key) where TSelection : class, T
    {
        if (Children.Length == 0) return Array.Empty<TSelection>();
        var searchRoot = this;

        TailRecursionWhen:

        foreach(var child in searchRoot.Children)
        {
            if (key[0] != child.FirstChar) continue;

            var matchingCharacters = key.CommonPrefixLength(child.KeySegment);

            if (matchingCharacters == key.Length)
            {
                // We found a key that matched the entire prefix search
                return child.Collect<TSelection>();

            }
            else
            {

                if (matchingCharacters == child.KeySegment.Length)
                {
                    // We matched the whole child key, check the remainder of
                    // the prefix search against that child's children
                    key = key.Slice(matchingCharacters);
                    searchRoot = child;

                    // This could probably be rewritten as a while loop,
                    // but this feels like a good standin for what I'd actually
                    // like to do if C# supported tail recursion
                    goto TailRecursionWhen; 
                }

                // We partial matched, but the remainder is a mismatch
                return Array.Empty<TSelection>();

            }
        }

        return Array.Empty<TSelection>();
    }

    public int GetChildrenCount()
    {
        return GetChildrenCountInternal(0);
    }

    public int CountValues(int runningCount = 0)
    {
        if (Value != null) runningCount++;
        var children = Children;
        for (int i = 0; i < children.Length; i++)
        {
            Node<T> child = children[i];
            runningCount = child.CountValues(runningCount);
        }
        return runningCount;
    }

    private int GetChildrenCountInternal(int runningCount)
    {
        var children = Children;
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            runningCount++;
            runningCount = child.GetChildrenCountInternal(runningCount);
        }

        return runningCount;
    }

    public int GetValuesCount()
    {
        return GetValuesCountInternal(0);
    }

    private int GetValuesCountInternal(int runningCount)
    {
        foreach (var child in Children)
        {
            if (child.Value != null)
            {
                runningCount++;
            }
            runningCount = child.GetChildrenCountInternal(runningCount);
        }
        return runningCount;
    }

    public override string ToString()
    {
        return ToGraphNodeString();
    }
}