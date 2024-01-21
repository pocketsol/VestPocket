using System.Text;

namespace VestPocket;

/// <summary>
/// A typed Radix Tree Node containing a payload value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity stored within this node</typeparam>
internal class Node<T>
{
    public string Key;
    private ReadOnlyMemory<byte> KeySegment = EmptyBytes;
    public ReadOnlyMemory<byte> KeyBytes = EmptyBytes;

    public Node<T>[] childrenBuffer = EmptyNodes;
    private Span<Node<T>> Children => childrenBuffer.AsSpan();
    public T Value;
    public byte FirstKeyByte;

    public static readonly Node<T>[] EmptyNodes = Array.Empty<Node<T>>();
    public static readonly byte[] EmptyBytes = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindChildByFirstByte(byte searchKeyByte)
    {
        // The 'default' search is a basic binary search (see the 'default' case
        // But if we specialize and unroll for other size counts, those sizes
        // can have improved performance. 
        var buffer = childrenBuffer;
        int childCount = childrenBuffer.Length;

        switch (childCount)
        {
            case 0: return ~0;
            case 1:
                int cmp = buffer[0].FirstKeyByte - searchKeyByte;
                if (cmp == 0) return 0;
                return cmp > 0 ? ~0 : ~1;
            //if (buffer[0].FirstKeyByte == searchKeyByte) return 0;
            //if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
            //return ~1;
            case 2:
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                return ~2;
            case 3:
                if (buffer[2].FirstKeyByte == searchKeyByte) return 2;
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                if (buffer[2].FirstKeyByte > searchKeyByte) return ~2;
                return ~3;
            case 10:
                // Size 10 is very important. If this tree is used
                // to store codes and ids, then its likely many
                // nodes will only have children for decimals

                // Unroll loop and manually binary search
                int cmp5 = buffer[5].FirstKeyByte - searchKeyByte;
                if (cmp5 == 0) return 5;
                if (cmp5 < 0)
                {
                    int cmp8 = buffer[8].FirstKeyByte - searchKeyByte;
                    if (cmp8 == 0) return 8;
                    if (cmp8 < 0)
                    {
                        int cmp9 = buffer[9].FirstKeyByte - searchKeyByte;
                        if (cmp9 == 0) return 9;
                        if (cmp9 < 0) return ~9;
                        return ~8;
                    }

                    int cmp6 = buffer[6].FirstKeyByte - searchKeyByte;
                    if (cmp6 == 0) return 6;
                    if (cmp6 < 0)
                    {
                        int cmp7 = buffer[7].FirstKeyByte - searchKeyByte;
                        if (cmp7 == 0) return 7;
                        if (cmp7 < 0) return ~7;
                        return ~6;
                    }
                    return ~5;
                }

                int cmp2 = buffer[2].FirstKeyByte - searchKeyByte;
                if (cmp2 == 0) return 2;
                if (cmp2 < 0)
                {
                    int cmp3 = buffer[3].FirstKeyByte - searchKeyByte;
                    if (cmp3 == 0) return 3;
                    if (cmp3 < 0)
                    {
                        int cmp4 = buffer[4].FirstKeyByte - searchKeyByte;
                        if (cmp4 == 0) return 4;
                        if (cmp4 < 0) return ~4;
                        return ~3;
                    }
                    return ~2;
                }
                int cmp0 = buffer[0].FirstKeyByte - searchKeyByte;
                if (cmp0 == 0) return 0;
                if (cmp0 < 0)
                {
                    int cmp1 = buffer[1].FirstKeyByte - searchKeyByte;
                    if (cmp1 == 0) return 1;
                    if (cmp1 < 0) return ~1;
                    return ~0;
                };

                return -1;

            case 256:
                // If the node has every possible ordered value, no need to search
                return searchKeyByte;

            default:
                int lo = 0;
                int hi = childCount - 1;
                while (lo <= hi)
                {
                    int i = lo + (hi - lo >> 1);
                    int c = buffer[i].FirstKeyByte - searchKeyByte;
                    if (c == 0) return i;
                    if (c < 0)
                    {
                        lo = i + 1;
                    }
                    else
                    {
                        hi = i - 1;
                    }
                }
                return ~lo;
        }

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public KeyValue<T> AsKeyValue()
    {
        return new KeyValue<T>(Key!, KeyBytes, Value);
    }

    /// <summary>
    /// Steps down the child nodes of this node, creating missing key segments as necessary before
    /// setting the value on the last matching child node.
    /// </summary>
    /// <param name="rootNode">A reference to the root node. Necessary for one fringe concurrency case</param>
    /// <param name="key">The full key to set the value on</param>
    /// <param name="value">The value to set on the matching node</param>
    public void SetValue(ref Node<T> rootNode, ReadOnlySpan<byte> key, T value)
    {
        ref Node<T> searchNode = ref rootNode;
        var searchKey = key;
        int keyOffset = 0;
        byte[] keyBytes = null;

        // Every branch of this while statement must either set a new descendant search node or return
        while (true)
        {
            searchKey = key.Slice(keyOffset);
            var searchKeyByte = searchKey[0];

            int matchingIndex = searchNode.FindChildByFirstByte(searchKeyByte);

            if (matchingIndex > -1)
            {
                ref Node<T> matchingChild = ref searchNode.childrenBuffer[matchingIndex]!;

                int matchingLength = 1;
                int childKeySegmentLength = matchingChild.KeySegment.Length;

                if (childKeySegmentLength > 1)
                {
                    var matchingChildKeySegment = matchingChild.KeySegment;
                    ReadOnlySpan<byte> matchingChildKey = matchingChildKeySegment.Span;
                    matchingLength = searchKey.CommonPrefixLength(matchingChildKey);
                }

                if (matchingLength == searchKey.Length)
                {
                    if (matchingLength == childKeySegmentLength)
                    {
                        // We found a child node that matches our key exactly
                        // E.g. Key = "apple" and child key = "apple"
                        matchingChild.Value = value;
                        return;
                    }
                    else
                    {
                        // We matched the whole set key, but not the entire child key. We need to split the child key
                        SplitNode(ref matchingChild, matchingLength);
                        matchingChild.Value = value;
                        return;
                    }

                }
                else // We matched part of the set key on a child
                {
                    if (matchingLength == childKeySegmentLength)
                    {
                        // and the entire child key
                        keyOffset += matchingLength;
                        searchNode = ref matchingChild;
                    }
                    else
                    {
                        // and only part of the child key
                        SplitNode(ref matchingChild, matchingLength);
                        keyOffset += matchingLength;
                        searchNode = ref matchingChild;
                    }
                }
            }
            else
            {
                // There were no matching children, lets add a new one.
                // E.g. Key = "apple" and no child that even starts with 'a'. Add a new child node

                // Binary search results returns bitwise complement of the index of the first
                // byte greater than the one we searched for (which is where we want to insert
                // our new child).
                int insertChildAtIndex = ~matchingIndex;
                if (keyBytes is null) keyBytes = key.ToArray();
                var newChild = new Node<T>();

                newChild.KeySegment = keyBytes.AsMemory(keyOffset, searchKey.Length);
                newChild.FirstKeyByte = keyBytes[keyOffset];
                newChild.KeyBytes = keyBytes.AsMemory(0, keyOffset + searchKey.Length);
                newChild.Key = Encoding.UTF8.GetString(newChild.KeyBytes.Span);
                newChild.Value = value;
                searchNode = searchNode.CloneWithNewChild(newChild, insertChildAtIndex);

                return;
            }
        }
    }

    public Node<T> Clone(bool copyChildren)
    {
        var clone = new Node<T>();
        clone.Key = Key;
        clone.KeySegment = KeySegment;
        clone.FirstKeyByte = FirstKeyByte;
        clone.KeyBytes = KeyBytes;
        clone.Value = Value;
        if (copyChildren)
        {
            clone.childrenBuffer = childrenBuffer;
        }
        return clone;
    }

    private static readonly int[] ChildBuffersSizeLUT = GetChildBufferCapacitySizes();
    private static int[] GetChildBufferCapacitySizes()
    {
        // The purpose of this lookup table is to assign child array sizes by bucketing
        // around the assumption that many keys will map to ASCII based usage patterns.
        int[] sizes = new int[256];
        for (int i = 0; i < sizes.Length; i++)
        {
            if (i == 0)
            {
                sizes[i] = 0;
            }
            else if (i == 1)
            {
                sizes[i] = 1;
            }
            else if (i < 4)
            {
                sizes[i] = 4;
            }
            else if (i < 11) // Decimals
            {
                // Including a space for an
                // arbitrary delimiter
                sizes[i] = 10;
            }
            else if (i < 16) // GUID
            {
                // While 17 characters are possible,
                // only 16 unique combinations can be at any
                // given location within a string representation of
                // a guid (as the hyphens are always at the same ordinals).
                sizes[i] = 16;
            }
            else if (i < 32) // Alpha characters
            {
                // Enough to hold either uppercase or lowercase
                // plus a couple delimiters, and added
                // a few extra to round up to nearest power of 2
                sizes[i] = 32;
            }
            else if (i < 65) // Base64
            {
                sizes[i] = 65;
            }
            else if (i < 95) // Printable ASCII
            {
                sizes[i] = 95;
            }
            else if (i < 128)
            {
                // Jump to full 128 or 256
                // If we are using more than 95 unique byte values
                // then there is a good chance we are storing something
                // that is not heavily ascii based.
                sizes[i] = 128;
            }
            else
            {
                sizes[i] = 256;
            }
        }
        return sizes;
    }

    public Node<T> CloneWithNewChild(Node<T> newChild, int atIndex)
    {
        var clone = Clone(false);
        clone.childrenBuffer = new Node<T>[childrenBuffer.Length + 1];
        Children.CopyWithInsert(clone.Children, newChild, atIndex);
        return clone;
    }

    private static void SplitNode(ref Node<T> child, int atKeyLength)
    {
        // We are taking a child node and splitting it at a specific number of
        // characters in its key segment

        // E.g.
        // We have nodes A => BC => ...
        // and we want A => B => C => etc..
        // A is the current 'this' node of this method
        // BC is the original child node
        // B is the splitParent, and gets a null value
        // C is the new splitChild, it retains the original value and children of the 'BC' node

        // We have to clone the child we split because we are changing its key size
        var splitChild = child.Clone(true);
        var newOffset = atKeyLength;
        var newCount = splitChild.KeySegment.Length - atKeyLength;
        splitChild.KeySegment = splitChild.KeySegment.Slice(newOffset, newCount);
        splitChild.FirstKeyByte = splitChild.KeySegment.Span[0];

        var splitParent = new Node<T>();
        splitParent.childrenBuffer = [splitChild];

        var childKeySegment = child.KeySegment;
        splitParent.KeySegment = childKeySegment.Slice(0, atKeyLength);
        splitParent.FirstKeyByte = childKeySegment.Span[0];
        var childKey = child.KeyBytes;
        splitParent.KeyBytes = childKey.Slice(0, childKey.Length - childKeySegment.Length + atKeyLength);
        splitParent.Key = Encoding.UTF8.GetString(splitParent.KeyBytes.Span);

        child = splitParent;
    }


    public T Get(ReadOnlySpan<byte> key)
    {
        var searchNode = this;
        var searchKeyByte = key[0];
        int matchingIndex = searchNode.FindChildByFirstByte(searchKeyByte);

        while (matchingIndex > -1)
        {
            searchNode = searchNode.childrenBuffer[matchingIndex];

            var keySegment = searchNode.KeySegment.Span;
            var keyLength = keySegment.Length;
            var bytesMatched = keyLength == 1 ? 1 : key.CommonPrefixLength(keySegment);

            if (bytesMatched == key.Length)
            {
                return bytesMatched == keyLength ? searchNode.Value : default;
            }
            key = key.Slice(bytesMatched);
            searchKeyByte = key[0];
            matchingIndex = searchNode.FindChildByFirstByte(searchKeyByte);
        }
        return default;
    }

    internal Node<T> FindPrefixMatch(ReadOnlySpan<byte> key)
    {
        var node = this;
        var childIndex = node.FindChildByFirstByte(key[0]);
        while (childIndex > -1)
        {
            node = node.childrenBuffer[childIndex];
            var keySegment = node.KeySegment.Span;
            var keyLength = keySegment.Length;
            var matchingBytes = keyLength == 1 ? 1 : key.CommonPrefixLength(keySegment);

            if (matchingBytes == key.Length) { return node; }

            // We have to match the whole child key to be a match
            if (matchingBytes != keyLength) return null;

            key = key.Slice(matchingBytes);

            childIndex = node.FindChildByFirstByte(key[0]);
        }
        return null;
    }

    public int GetValuesCount()
    {
        int runningCount = 0;
        GetValuesCountInternal(ref runningCount);
        return runningCount;
    }

    private void GetValuesCountInternal(ref int runningCount)
    {
        if (Value is not null) runningCount++;
        foreach (var child in childrenBuffer)
        {
            child.GetValuesCountInternal(ref runningCount);
        }
    }

    public override string ToString()
    {
        return Key!;
    }

    internal void Reset()
    {
        KeyBytes = default;
        Value = default;
        childrenBuffer = EmptyNodes;
        KeySegment = EmptyBytes;
    }


}