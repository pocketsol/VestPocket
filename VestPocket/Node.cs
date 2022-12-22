using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace VestPocket;

/// <summary>
/// A typed Radix Tree Node containing a payload value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity stored within this node</typeparam>
internal class Node<T> where T : class, IEntity
{
    public Node<T> Parent = null;
    public string KeySegment;
    public Node<T>[] Children;
    public T Value;

    public Node()
    {
        this.KeySegment = String.Empty;
    }

    public Node(Node<T> parent, string label)
    {
        this.Parent = parent;
        this.KeySegment = label;
    }

    public void SetValue(ReadOnlySpan<char> key, T value)
    {

        // Set on a child
        var matchingChild = FindMatchingChild(key, out var matchingLength);
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
                    matchingChild.SplitKeySegmentAtLength(matchingLength);
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
                    matchingChild.SplitKeySegmentAtLength(matchingLength);
                    matchingChild.SetValue(key.Slice(matchingLength), value);
                }                    
            }
        }
        else
        {
            // There were no matching children. 
            // E.g. Key = "apple" and no child that even starts with 'a'. Add a new child node
            string keySegment = new string( key.Slice(0));
            var newChild = new Node<T>(this, keySegment);
            newChild.Value = value;

            AddChild(newChild);
        }

    }

    private void AddChild(Node<T> newChild)
    {
        if (Children == null)
        {
            Children = new Node<T>[1];
        }
        else
        {
            Array.Resize(ref Children, Children.Length + 1);
        }
        Children[^1] = newChild;
    }

    public void SplitKeySegmentAtLength(int startingCharacter)
    {
        // Create new split child
        var newChildKeySegment = KeySegment.Substring(startingCharacter);
        var newChild = new Node<T>(this, newChildKeySegment) { Value = this.Value };

        this.Value = null;
        if (Children == null) {
            AddChild(newChild);
        }
        else
        {

            //push existing children down
            newChild.Children = Children;
            for(int i = 0; i < newChild.Children.Length; i++)
            {
                Children[i].Parent = newChild;
            }
            this.Children = null;
            AddChild(newChild);
        }

        // Change this nodes segment to portion that wasn't cutoff
        KeySegment = KeySegment.Substring(0, startingCharacter);

    }

    public Node<T> GetValue(ReadOnlySpan<char> key)
    {
        if (Children == null) return null;
        //var remainingKeyLength = key.Length - startingCharacter;

        foreach(var child in Children)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    private Node<T> FindMatchingChild(ReadOnlySpan<char> keySegment, out int bytesMatching)
    {
        if (Children != null)
        {
            foreach(var child in Children)
            {
                var matchingBytes = keySegment.CommonPrefixLength(child.KeySegment);

                if (matchingBytes > 0)
                {
                    bytesMatching = matchingBytes;
                    return child;
                }
            }
        }
        bytesMatching = 0;
        return null;
    }

    private void RemoveChild(Node<T> child)
    {
        Children = Children.Where(x => x != child).ToArray();
    }

    public void RemoveValue()
    {
        Value = null;
        if (Children == null)
        {
            if (Parent!.Children!.Length == 1)
            {
                Parent.Children = null;
            }
            else
            {
                Parent.RemoveChild(this);
            }
        }
        else
        {
            TryMergeChild();
            var parent = Parent;
            while (parent != null)
            {
                if (!parent.TryMergeChild())
                {
                    break;
                }
                parent = parent.Parent;
            }
        }
    }

    public bool TryMergeChild()
    {
        if (Children != null && Children.Length == 1)
        {
            var child = Children[0];
            Value = child.Value;
            Children = child.Children;
            KeySegment = KeySegment + child.KeySegment;
            return true;
        }
        return false;
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