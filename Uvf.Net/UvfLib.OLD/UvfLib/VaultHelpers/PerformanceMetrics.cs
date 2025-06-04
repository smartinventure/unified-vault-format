// UvfLib/VaultHelpers/PerformanceMetrics.cs
#if DEBUG
using System;
using System.Diagnostics; // For Stopwatch

namespace UvfLib.VaultHelpers // Ensure this namespace is correct
{
    internal class PerformanceMetrics
    {
        private readonly string _streamName;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        
        // Change properties to public fields to allow passing by ref
        public long TotalOperation1TimeMs = 0;
        public string Operation1Name { get; set; } = "Operation1";

        public long TotalOperation2TimeMs = 0;
        public string Operation2Name { get; set; } = "Operation2";

        public long TotalOperation3TimeMs = 0;
        public string Operation3Name { get; set; } = "Operation3";
        
        public long TotalOperation4TimeMs = 0;
        public string Operation4Name { get; set; } = "Operation4";

        public long ChunksProcessedCounter { get; private set; } = 0;

        public PerformanceMetrics(string streamName)
        {
            _streamName = streamName;
        }

        public void StartTiming() => _stopwatch.Restart();
        
        // accumulator is now a ref to a public field
        public void StopTiming(ref long accumulator) 
        {
            _stopwatch.Stop();
            accumulator += _stopwatch.ElapsedMilliseconds;
        }
        public void IncrementChunksProcessed() => ChunksProcessedCounter++;

        public void Report()
        {
            // Using Debug.WriteLine so output goes to the Debug Output window in IDEs
            // or other configured debug listeners.
            Debug.WriteLine($"--- {_streamName} Performance Metrics ---");
            if (ChunksProcessedCounter > 0)
            {
                if (!string.IsNullOrEmpty(Operation1Name) && TotalOperation1TimeMs >= 0) Debug.WriteLine($"Total {Operation1Name} Time: {TotalOperation1TimeMs} ms (Avg: {(double)TotalOperation1TimeMs / ChunksProcessedCounter:F4} ms/chunk)");
                if (!string.IsNullOrEmpty(Operation2Name) && TotalOperation2TimeMs >= 0) Debug.WriteLine($"Total {Operation2Name} Time: {TotalOperation2TimeMs} ms (Avg: {(double)TotalOperation2TimeMs / ChunksProcessedCounter:F4} ms/chunk)");
                if (!string.IsNullOrEmpty(Operation3Name) && TotalOperation3TimeMs >= 0) Debug.WriteLine($"Total {Operation3Name} Time: {TotalOperation3TimeMs} ms (Avg: {(double)TotalOperation3TimeMs / ChunksProcessedCounter:F4} ms/chunk)");
                if (!string.IsNullOrEmpty(Operation4Name) && TotalOperation4TimeMs > 0) Debug.WriteLine($"Total {Operation4Name} Time: {TotalOperation4TimeMs} ms (Avg: {(double)TotalOperation4TimeMs / ChunksProcessedCounter:F4} ms/chunk)");
                
                long totalMeasuredTime = TotalOperation1TimeMs + TotalOperation2TimeMs + TotalOperation3TimeMs + TotalOperation4TimeMs;
                Debug.WriteLine($"Total Measured Time in Chunks: {totalMeasuredTime} ms");
            }
            else
            {
                Debug.WriteLine("No chunks processed for detailed metrics.");
            }
            Debug.WriteLine($"------------------------------------------");
        }
    }
}
#endif 