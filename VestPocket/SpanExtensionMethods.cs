namespace VestPocket
{
    internal static class SpanExtensionMethods
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyWithInsert<T>(this Span<T> source, Span<T> destination, T insertValue, int atIndex)
        {

            for (int i = 0; i < destination.Length; i++)
            {
                if (i == atIndex)
                {
                    destination[i] = insertValue;
                }
                else if (i < atIndex)
                {
                    destination[i] = source[i];
                }
                else
                {
                    destination[i] = source[i - 1];
                }
            }
        }

    }
}
