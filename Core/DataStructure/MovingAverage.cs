namespace LegendaryTools
{
    public class MovingAverage
    {
        public float Average
        {
            get
            {
                if (count == 0)
                {
                    return 0f;
                }

                return sum / count;
            }
        }
        
        public int Count => count;
        public int Size => buffer.Length;
        
        private readonly float[] buffer;
        private int index;
        private int count;
        private float sum;
        
        public MovingAverage(int size)
        {
            if (size <= 0)
            {
                throw new System.ArgumentException("O tamanho deve ser maior que zero.", nameof(size));
            }

            buffer = new float[size];
            index = 0;
            count = 0;
            sum = 0f;
        }
        
        public void Add(float value)
        {
            if (count < buffer.Length)
            {
                buffer[index] = value;
                sum += value;
                count++;
            }
            else
            {
                sum -= buffer[index];
                buffer[index] = value;
                sum += value;
            }
            
            index = (index + 1) % buffer.Length;
        }
    }
}
