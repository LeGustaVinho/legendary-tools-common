namespace LegendaryTools.Deterministic
{
    // Built-in storage can move prepared ranges in blocks without extending the public buffer contract.
    internal interface IDeterministicMovement<T, TBuffer>
        where T : unmanaged where TBuffer : struct, IDeterministicBuffer<T>
    {
        void Move(ref TBuffer buffer, int source, int destination, int count);
    }

    internal struct ElementwiseMovement<T, TBuffer> : IDeterministicMovement<T, TBuffer>
        where T : unmanaged where TBuffer : struct, IDeterministicBuffer<T>
    {
        public void Move(ref TBuffer buffer, int source, int destination, int count)
        {
            if (destination > source && destination < (long)source + count)
                for (int i = count - 1; i >= 0; i--) { var value = buffer.Read(source + i); buffer.Write(destination + i, value); }
            else
                for (int i = 0; i < count; i++) { var value = buffer.Read(source + i); buffer.Write(destination + i, value); }
        }
    }
}
