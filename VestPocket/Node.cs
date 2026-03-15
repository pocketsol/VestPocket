using System.Runtime.CompilerServices;
using System.Text;

namespace VestPocket;

/// <summary>
/// A typed Radix Tree Node containing a payload value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The entity stored within this node</typeparam>
internal class Node<T>
{
    public string Key;
    private byte[] keyBytes = EmptyBytes;
    private int keyBytesLength;
    private int keySegmentStart;

    public Node<T>[] childrenBuffer = EmptyNodes;
    private Span<Node<T>> Children => childrenBuffer.AsSpan();
    public T Value;
    public byte FirstKeyByte;

    public static readonly Node<T>[] EmptyNodes = Array.Empty<Node<T>>();
    private static readonly byte[] EmptyBytes = [];

    private int KeySegmentLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => keyBytesLength - keySegmentStart;
    }

    private ReadOnlySpan<byte> KeySegmentSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => keyBytes.AsSpan(keySegmentStart, keyBytesLength - keySegmentStart);
    }

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
            case 4:
                if (buffer[3].FirstKeyByte == searchKeyByte) return 3;
                if (buffer[2].FirstKeyByte == searchKeyByte) return 2;
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                if (buffer[2].FirstKeyByte > searchKeyByte) return ~2;
                if (buffer[3].FirstKeyByte > searchKeyByte) return ~3;
                return ~4;
            case 5:
                if (buffer[4].FirstKeyByte == searchKeyByte) return 4;
                if (buffer[3].FirstKeyByte == searchKeyByte) return 3;
                if (buffer[2].FirstKeyByte == searchKeyByte) return 2;
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                if (buffer[2].FirstKeyByte > searchKeyByte) return ~2;
                if (buffer[3].FirstKeyByte > searchKeyByte) return ~3;
                if (buffer[4].FirstKeyByte > searchKeyByte) return ~4;
                return ~5;

            case 6:
                int cmp6_2 = buffer[2].FirstKeyByte - searchKeyByte;
                if (cmp6_2 == 0) return 2;
                if (cmp6_2 < 0)
                {
                    if (buffer[5].FirstKeyByte == searchKeyByte) return 5;
                    if (buffer[4].FirstKeyByte == searchKeyByte) return 4;
                    if (buffer[3].FirstKeyByte == searchKeyByte) return 3;
                    if (buffer[3].FirstKeyByte > searchKeyByte) return ~3;
                    if (buffer[4].FirstKeyByte > searchKeyByte) return ~4;
                    if (buffer[5].FirstKeyByte > searchKeyByte) return ~5;
                    return ~6;
                }

                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                return ~2;

            case 7:
                int cmp7_3 = buffer[3].FirstKeyByte - searchKeyByte;
                if (cmp7_3 == 0) return 3;
                if (cmp7_3 < 0)
                {
                    if (buffer[6].FirstKeyByte == searchKeyByte) return 6;
                    if (buffer[5].FirstKeyByte == searchKeyByte) return 5;
                    if (buffer[4].FirstKeyByte == searchKeyByte) return 4;
                    if (buffer[4].FirstKeyByte > searchKeyByte) return ~4;
                    if (buffer[5].FirstKeyByte > searchKeyByte) return ~5;
                    if (buffer[6].FirstKeyByte > searchKeyByte) return ~6;
                    return ~7;
                }

                if (buffer[2].FirstKeyByte == searchKeyByte) return 2;
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                if (buffer[2].FirstKeyByte > searchKeyByte) return ~2;
                return ~3;
            case 8:
                int cmp8_4 = buffer[4].FirstKeyByte - searchKeyByte;
                if (cmp8_4 == 0) return 4;
                if (cmp8_4 < 0)
                {
                    if (buffer[7].FirstKeyByte == searchKeyByte) return 7;
                    if (buffer[6].FirstKeyByte == searchKeyByte) return 6;
                    if (buffer[5].FirstKeyByte == searchKeyByte) return 5;

                    if (buffer[5].FirstKeyByte > searchKeyByte) return ~5;
                    if (buffer[6].FirstKeyByte > searchKeyByte) return ~6;
                    if (buffer[7].FirstKeyByte > searchKeyByte) return ~7;
                    return ~8;
                }

                if (buffer[3].FirstKeyByte == searchKeyByte) return 3;
                if (buffer[2].FirstKeyByte == searchKeyByte) return 2;
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                if (buffer[2].FirstKeyByte > searchKeyByte) return ~2;
                if (buffer[3].FirstKeyByte > searchKeyByte) return ~3;
                return ~4;
            case 9:
                int cmp9_4 = buffer[4].FirstKeyByte - searchKeyByte;
                if (cmp9_4 == 0) return 4;
                if (cmp9_4 < 0)
                {
                    if (buffer[8].FirstKeyByte == searchKeyByte) return 8;
                    if (buffer[7].FirstKeyByte == searchKeyByte) return 7;
                    if (buffer[6].FirstKeyByte == searchKeyByte) return 6;
                    if (buffer[5].FirstKeyByte == searchKeyByte) return 5;

                    if (buffer[5].FirstKeyByte > searchKeyByte) return ~5;
                    if (buffer[6].FirstKeyByte > searchKeyByte) return ~6;
                    if (buffer[7].FirstKeyByte > searchKeyByte) return ~7;
                    if (buffer[8].FirstKeyByte > searchKeyByte) return ~8;
                    return ~9;
                }

                if (buffer[3].FirstKeyByte == searchKeyByte) return 3;
                if (buffer[2].FirstKeyByte == searchKeyByte) return 2;
                if (buffer[1].FirstKeyByte == searchKeyByte) return 1;
                if (buffer[0].FirstKeyByte == searchKeyByte) return 0;

                if (buffer[0].FirstKeyByte > searchKeyByte) return ~0;
                if (buffer[1].FirstKeyByte > searchKeyByte) return ~1;
                if (buffer[2].FirstKeyByte > searchKeyByte) return ~2;
                if (buffer[3].FirstKeyByte > searchKeyByte) return ~3;
                return ~4;

            case 10:
                // Size 10 is very important. If this tree is used
                // to store codes and ids, then its likely many
                // nodes will only have children for decimals

                // Unroll loop and manually binary search
                int cmp5 = buffer[5].FirstKeyByte - searchKeyByte;
                if (cmp5 == 0) return 5;
                if (cmp5 < 0) // Its greater than index 5
                {
                    int cmp8 = buffer[8].FirstKeyByte - searchKeyByte;
                    if (cmp8 == 0) return 8;
                    if (cmp8 < 0)
                    {
                        int cmp9 = buffer[9].FirstKeyByte - searchKeyByte;
                        if (cmp9 == 0) return 9;
                        if (cmp9 < 0) return ~10; // No value greater than search value, return bitwise complement of the last index, plus one
                        return ~9;
                    }

                    // Its greater than 5 but less than 8

                    int cmp6 = buffer[6].FirstKeyByte - searchKeyByte;
                    if (cmp6 == 0) return 6;
                    if (cmp6 < 0) // Its value is between index 6 and 8 exclusive
                    {
                        int cmp7 = buffer[7].FirstKeyByte - searchKeyByte;
                        if (cmp7 == 0) return 7;
                        if (cmp7 < 0) return ~8; // Its value is between index 7 and 8
                        return ~7;
                    }
                    // Its greater than 5, but less than 6
                    return ~6;
                }

                int cmp2 = buffer[2].FirstKeyByte - searchKeyByte;
                if (cmp2 == 0) return 2;
                if (cmp2 < 0) // Its between index 2 and 5 exclusive
                {
                    int cmp3 = buffer[3].FirstKeyByte - searchKeyByte;
                    if (cmp3 == 0) return 3;
                    if (cmp3 < 0) // Its between index 3 and 5 exclusive
                    {
                        int cmp4 = buffer[4].FirstKeyByte - searchKeyByte;
                        if (cmp4 == 0) return 4;
                        if (cmp4 < 0) return ~5; // Its greater than 4, but less than 5
                        return ~4;
                    }
                    // Its greater than two, but less than 3
                    return ~3;
                }

                // Its less than index 2
                int cmp0 = buffer[0].FirstKeyByte - searchKeyByte;
                if (cmp0 == 0) return 0;
                if (cmp0 < 0) //Its less than index 2, but greater than index 0
                {
                    int cmp1 = buffer[1].FirstKeyByte - searchKeyByte;
                    if (cmp1 == 0) return 1;
                    if (cmp1 < 0) return ~2;
                    return ~1;
                }

                return ~0;

            case 256:
                // If the node has every possible ordered value, no need to search
                return searchKeyByte;

            default:
                int lo = 0;
                int hi = childCount - 1;

                while (lo <= hi)
                {
                    int i = (lo + hi) >> 1;
                    byte v = buffer[i].FirstKeyByte;

                    if (v == searchKeyByte)
                        return i;

                    lo = v < searchKeyByte ? i + 1 : lo;
                    hi = v > searchKeyByte ? i - 1 : hi;
                }
                return ~lo;
        }

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public KeyValue<T> AsKeyValue()
    {
        return new KeyValue<T>(Key!, keyBytes.AsMemory(0, keyBytesLength), Value);
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
                int childKeySegmentLength = matchingChild.KeySegmentLength;

                if (childKeySegmentLength > 1)
                {
                    ReadOnlySpan<byte> matchingChildKey = matchingChild.KeySegmentSpan;
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

                newChild.keyBytes = keyBytes;
                newChild.keySegmentStart = keyOffset;
                newChild.keyBytesLength = keyOffset + searchKey.Length;
                newChild.FirstKeyByte = keyBytes[keyOffset];
                newChild.Key = Encoding.UTF8.GetString(keyBytes, 0, newChild.keyBytesLength);
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
        clone.keyBytes = keyBytes;
        clone.keyBytesLength = keyBytesLength;
        clone.keySegmentStart = keySegmentStart;
        clone.FirstKeyByte = FirstKeyByte;
        clone.Value = Value;
        if (copyChildren)
        {
            clone.childrenBuffer = childrenBuffer;
        }
        return clone;
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
        splitChild.keySegmentStart += atKeyLength;
        splitChild.FirstKeyByte = splitChild.keyBytes[splitChild.keySegmentStart];

        var splitParent = new Node<T>();
        splitParent.childrenBuffer = [splitChild];

        splitParent.keyBytes = child.keyBytes;
        splitParent.keySegmentStart = child.keySegmentStart;
        splitParent.keyBytesLength = child.keySegmentStart + atKeyLength;
        splitParent.FirstKeyByte = child.keyBytes[child.keySegmentStart];
        splitParent.Key = Encoding.UTF8.GetString(splitParent.keyBytes, 0, splitParent.keyBytesLength);

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

            var keySegment = searchNode.KeySegmentSpan;
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
            var keySegment = node.KeySegmentSpan;
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
        keyBytes = EmptyBytes;
        keyBytesLength = 0;
        keySegmentStart = 0;
        Value = default;
        childrenBuffer = EmptyNodes;
    }


}
