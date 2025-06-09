using System.Diagnostics;
using FolderMagicLib.Logging;

namespace UvfLib.Storage.Abstractions.Throttling
{
    public class ThrottledStreamStopWatch : Stream
    {
        private static readonly Logging.Logging _logger = Logging.Logging.Instance;
        public const long Infinite = 0;

        private readonly Stream _baseStream;
        private long _maximumBytesPerSecond;
        private long _byteCount;
        private readonly Stopwatch _stopwatch;

        public long MaximumBytesPerSecond
        {
            get => _maximumBytesPerSecond;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "MaximumBytesPerSecond cannot be negative.");
                }
                if (_maximumBytesPerSecond != value)
                {
                    _maximumBytesPerSecond = value;
                    Reset();
                }
            }
        }

        public override bool CanRead => _baseStream.CanRead;

        public override bool CanSeek => _baseStream.CanSeek;

        public override bool CanWrite => _baseStream.CanWrite;

        public override long Length => _baseStream.Length;

        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public ThrottledStreamStopWatch(Stream baseStream)
            : this(baseStream, Infinite)
        {
        }

        public ThrottledStreamStopWatch(Stream baseStream, long maximumBytesPerSecond)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if (maximumBytesPerSecond < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            _maximumBytesPerSecond = maximumBytesPerSecond;
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
            _byteCount = 0;
        }

        public override void Flush() => _baseStream.Flush();

        public new Task FlushAsync() => _baseStream.FlushAsync();

        public override Task FlushAsync(CancellationToken cancellationToken) => _baseStream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            Throttle(count);
            return _baseStream.Read(buffer, offset, count);
        }

        public new async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            await ThrottleAsync(count);
            return await _baseStream.ReadAsync(buffer, offset, count).ConfigureAwait(false);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await ThrottleAsync(count);
            return await _baseStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);

        public override void SetLength(long value) => _baseStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Throttle(count);
            _baseStream.Write(buffer, offset, count);
        }

        public new async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            await ThrottleAsync(count);
            await _baseStream.WriteAsync(buffer, offset, count).ConfigureAwait(false);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await ThrottleAsync(count);
            await _baseStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public override string? ToString() => _baseStream.ToString();

        protected async Task ThrottleAsync(int bufferSizeInBytes)
        {
            if (_maximumBytesPerSecond <= 0 || bufferSizeInBytes <= 0)
            {
                return;
            }

            _byteCount += bufferSizeInBytes;
            long elapsedMilliseconds = _stopwatch.ElapsedMilliseconds;

            if (elapsedMilliseconds <= 0)
            {
                return;
            }

            long bps = _byteCount * 1000L / elapsedMilliseconds;
            if (bps <= _maximumBytesPerSecond)
            {
                return;
            }

            long wakeElapsed = _byteCount * 1000L / _maximumBytesPerSecond;
            int toSleep = (int)(wakeElapsed - elapsedMilliseconds);

            if (toSleep <= 1)
            {
                return;
            }

            try
            {
                await Task.Delay(toSleep);
            }
            catch (Exception e)
            {
                _logger.LogError("Error: " + e.Message);
            }

            Reset();
        }

        protected void Throttle(int bufferSizeInBytes) => ThrottleAsync(bufferSizeInBytes).Wait();

        protected void Reset()
        {
            if (_stopwatch.ElapsedMilliseconds > 1000)
            {
                _byteCount = 0;
                _stopwatch.Restart();
            }
        }
    }
}