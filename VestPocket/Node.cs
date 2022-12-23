namespace VestPocket;

/// <summary>
/// A typed Radix Tree Node containing a payload value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity stored within this node</typeparam>
internal class Node<T> where T : class, IEntity
{
    public string KeySegment;
    public Node<T>[] Children;
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
                    matchingChild.SetValue(key.Slice(matchingLength), value);
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
            newChild.Value = value;

            AddChild(newChild);
        }

    }

    private void AddChild(Node<T> newChild)
    {
        if (Children == null)
        {
            var newChildArray = new Node<T>[1] { newChild };
            Children = newChildArray;
        }
        else
        {
            var newLength = Children.Length + 1;
            var newChildArray = new Node<T>[newLength];
            Array.Copy(Children, newChildArray, Children.Length);
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
        var splitChild = new Node<T>() { KeySegment = splitChildKey, Value = child.Value };
        splitChild.Children = child.Children;

        var splitParentKey = child.KeySegment.Substring(0, startingCharacter);
        var splitParent = new Node<T>(splitParentKey) { Children = new Node<T>[] { splitChild } };

        Children[childIndex] = splitParent;
    }

    public Node<T> GetValue(ReadOnlySpan<char> key)
    {
        if (Children == null) return null;
        //var remainingKeyLength = key.Length - startingCharacter;

        foreach (var child in Children)
        {
            var matchingBytes = key.CommonPrefixLength(child.KeySegment);
            //var matchingBytes = GetMatchingBytes(key, child.KeySegment);

            if (matchingBytes > 0)
            {
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
        }
        return null;
    }

    public void GetValuesByPrefix<TSelection>(ReadOnlySpan<char> key, PrefixResult<TSelection> result) where TSelection : class, T
    {
        if (Children != null)
        {
            foreach (var child in Children)
            {
                var matchingCharacters = key.CommonPrefixLength(child.KeySegment);
                if (matchingCharacters > 0)
                {
                    if (matchingCharacters == key.Length)
                    {
                        // We found a key that matched the entire prefix,
                        // either exactly or at least to the length of the search key

                        child.CollectValues(result);
                    }
                    else if (matchingCharacters < key.Length)
                    {
                        child.GetValuesByPrefix(key.Slice(matchingCharacters), result);
                    }
                }
            }
        }
    }

    public void CollectValues<TSelection>(PrefixResult<TSelection> result) where TSelection : class, T
    {
        result.SetSize(CountValues());
        CollectValuesRecursive(result);
    }

    private void CollectValuesRecursive<TSelection>(PrefixResult<TSelection> result) where TSelection : class, T
    {
        if (Value != null) result.Add((TSelection)this.Value);
        if (Children == null) return;

        foreach (var child in Children)
        {
            // Instead of just using this:
            // child.CollectValues(result);

            // For performance reasons in this hotpath method,
            // We unroll a few layers of child values before recursing

            if (child.Value != null) result.Add((TSelection)child.Value);
            if (child.Children == null) continue;

            foreach (var child2 in child.Children)
            {
                if (child2.Value != null) result.Add((TSelection)child2.Value);
                if (child2.Children == null) continue;

                foreach (var child3 in child2.Children)
                {
                    if (child3.Value != null) result.Add((TSelection)child3.Value);
                    if (child3.Children == null) continue;

                    foreach (var child4 in child3.Children)
                    {
                        child4.CollectValuesRecursive(result);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Looks for a child node that has a key segment that matches part of the prefix of a given key segment
    /// </summary>
    /// <param name="keySegment"></param>
    /// <param name="result">The matching child node</param>
    /// <returns>The number of matching bytes</returns>
    private Node<T> FindMatchingChild(ReadOnlySpan<char> keySegment, out int bytesMatching, out int matachingIndex)
    {
        if (Children != null)
        {
            for (int i = 0; i < Children.Length; i++)
            {
                Node<T> child = Children[i];
                var matchingBytes = keySegment.CommonPrefixLength(child.KeySegment);

                if (matchingBytes > 0)
                {
                    bytesMatching = matchingBytes;
                    matachingIndex = i;
                    return child;
                }
            }
        }
        bytesMatching = 0;
        matachingIndex = -1;
        return null;
    }

    public int GetChildrenCount()
    {
        return GetChildrenCountInternal(0);
    }

    public int CountValues(int runningCount = 0)
    {
        if (Value != null) runningCount++;

        if (Children != null)
        {
            for (int i = 0; i < Children.Length; i++)
            {
                Node<T> child = Children[i];
                runningCount = child.CountValues(runningCount);
            }
        }
        return runningCount;
    }

    private int GetChildrenCountInternal(int runningCount)
    {
        if (Children != null)
        {
            foreach (var child in Children)
            {
                runningCount++;
                runningCount = child.GetChildrenCountInternal(runningCount);
            }
        }
        return runningCount;
    }

    public int GetValuesCount()
    {
        return GetValuesCountInternal(0);
    }

    private int GetValuesCountInternal(int runningCount)
    {
        if (Children != null)
        {
            foreach (var child in Children)
            {
                if (child.Value != null)
                {
                    runningCount++;
                }
                runningCount = child.GetChildrenCountInternal(runningCount);
            }
        }
        return runningCount;
    }

}