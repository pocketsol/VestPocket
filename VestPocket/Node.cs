using System.Security.Cryptography.X509Certificates;

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

    public Node()
    {
        this.KeySegment = String.Empty;
    }

    public Node(string label)
    {
        this.KeySegment = label;
    }

    public void SetValue(ReadOnlySpan<char> key, T value)
    {
        // Set on a child
        var matchingChild = FindMatchingChild(key, out var matchingLength, out var matchingIndex);
        if (matchingChild != null)
        {
            if (matchingLength == key.Length)
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
                    SplitChild(matchingLength, matchingIndex);
                    matchingChild = Children[matchingIndex];
                    matchingChild.Value = value;
                }

            }
            else
            {
                // We matched part of the set key on a child
                if (matchingLength == matchingChild.KeySegment.Length)
                {
                    // and the entire child key
                    matchingChild.SetValue(key.Slice(matchingLength), value);
                }
                else
                {
                    // and only part of the child key
                    //matchingChild.SplitKeySegmentAtLength(matchingLength);
                    SplitChild(matchingLength, matchingIndex);
                    matchingChild = Children[matchingIndex];
                    matchingChild.SetValue(key.Slice(matchingLength), value);

                }
            }
        }
        else
        {
            // There were no matching children. 
            // E.g. Key = "apple" and no child that even starts with 'a'. Add a new child node
            string keySegment = new string(key);
            var newChild = new Node<T>(keySegment);
            newChild.Parent = this;
            newChild.Value = value;
            AddChild(newChild);
        }

    }

    private void AddChild(Node<T> newChild)
    {
        if (Children.Length == 0)
        {
            newChild.ParentIndex = 0;
            var newChildArray = new Node<T>[1] { newChild };
            Children = newChildArray;
        }
        else
        {
            var newLength = Children.Length + 1;
            var newChildArray = new Node<T>[newLength];
            Array.Copy(Children, newChildArray, Children.Length);
            newChild.ParentIndex = newLength -1;
            newChildArray[^1] = newChild;
            Children = newChildArray;
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
        var splitChild = new Node<T>() { KeySegment = splitChildKey, Value = child.Value, Children = child.Children};


        var splitParentKey = child.KeySegment.Substring(0, startingCharacter);
        var splitParent = new Node<T>(splitParentKey) { Children = new Node<T>[] { splitChild } };

        splitParent.Parent = this;
        splitParent.ParentIndex = childIndex;
        splitChild.Parent = splitParent;
        splitChild.ParentIndex = 0;
        foreach (var splitChildChild in splitChild.Children)
        {
            splitChildChild.Parent = splitChild;
        }
        Children[childIndex] = splitParent;
    }

    private Node<T> Parent;
    private int ParentIndex;

    public Node<T> GetValue(ReadOnlySpan<char> key)
    {
        foreach (var child in Children)
        {
            if (key[0] != child.KeySegment[0]) continue;

            var matchingBytes = key.CommonPrefixLength(child.KeySegment);
            //var matchingBytes = GetMatchingBytes(key, child.KeySegment);

            if (matchingBytes == key.Length)
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
            else if (matchingBytes < key.Length)
            {
                return child.GetValue(key.Slice(matchingBytes));
            }
        }
        return null;
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

        var searchNode = Children[0];
        while(true)
        {
            if (searchNode == this)
            {
                yield break;
            }

            if (searchNode.Value is not null)
            {
                yield return (TSelection)searchNode.Value;
            }
            if (searchNode.Children.Length != 0)
            {
                // Dig depth first
                searchNode = searchNode.Children[0];
                continue;
            }
            else
            {
                // We hit a leaf, transverse upwards through parents until we find the next child
                // that hasen't been visited
                while(true)
                {
                    if (searchNode == this)
                    {
                        yield break;
                    }
                    var nextIndex = searchNode.ParentIndex + 1;
                    if (nextIndex < searchNode.Parent.Children.Length)
                    {
                        searchNode = searchNode.Parent.Children[nextIndex];
                        break;
                    }
                    else
                    {
                        // We transversed all the children of the parent of our search node.
                        // So we'll continue up from the parent
                        //var tmp = searchNode;
                        searchNode = searchNode.Parent;

                        if (searchNode == this)
                        {
                            yield break;
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
            if (key[0] != child.KeySegment[0]) continue;
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
                    goto TailRecursionWhen;
                }

                // We partial matched, but the remainder is a mismatch
                return Array.Empty<TSelection>();

            }
        }

        return Array.Empty<TSelection>();
    }

    //public void GetValuesByPrefix<TSelection>(ReadOnlySpan<char> key, PrefixResult<TSelection> result) where TSelection : class, T
    //{
    //    foreach (var child in Children)
    //    {

    //        if (key[0] != child.KeySegment[0]) continue;
    //        var matchingCharacters = key.CommonPrefixLength(child.KeySegment);

    //        if (matchingCharacters < child.KeySegment.Length)
    //        {
    //            // We found a node that shares characters of our key, but is longer than it, 
    //            // meaning we have no results.
    //            return;
    //        }
    //        if (matchingCharacters == key.Length)
    //        {
    //            // We found a key that matched the entire prefix,
    //            // either exactly or at least to the length of the search key

    //            child.CollectValues(result);
    //        }
    //        else if (matchingCharacters < key.Length)
    //        {
    //            child.GetValuesByPrefix(key.Slice(matchingCharacters), result);
    //        }
    //        return;
    //    }
    //}

    //public void CollectValues<TSelection>(PrefixResult<TSelection> result) where TSelection : class, T
    //{
    //    CollectValuesRecursive(result);
    //}

    //private void CollectValuesRecursive<TSelection>(PrefixResult<TSelection> result) where TSelection : class, T
    //{
    //    if (Value != null) result.Add((TSelection)this.Value);
    //    var children = Children;
    //    foreach (var child in children)
    //    {
    //        child.CollectValuesRecursive(result);
    //    }
    //}

    /// <summary>
    /// Looks for a child node that has a key segment that matches part of the prefix of a given key segment
    /// </summary>
    /// <param name="keySegment">The key segment to match on</param>
    /// <param name="bytesMatching">The number of matching bytes</param>
    /// <param name="matchingIndex">The index of the match</param>
    /// <returns></returns>
    private Node<T> FindMatchingChild(ReadOnlySpan<char> keySegment, out int bytesMatching, out int matchingIndex)
    {
        for (int i = 0; i < Children.Length; i++)
        {
            Node<T> child = Children[i];

             if (keySegment[0] != child.KeySegment[0]) continue;

            var matchingBytes = keySegment.CommonPrefixLength(child.KeySegment);

            if (matchingBytes > 0)
            {
                bytesMatching = matchingBytes;
                matchingIndex = i;
                return child;
            }
        }
        bytesMatching = 0;
        matchingIndex = -1;
        return null;
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
        return $"Key:{KeySegment} - ValueKey:{Value?.Key}";
    }
}