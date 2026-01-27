using System;
using System.Threading.Tasks;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Responsible for requesting remote UTC time via configured providers.
    /// </summary>
    public sealed class ChronosTimeSource
    {
        private readonly ChronosConfig config;

        public ChronosTimeSource(ChronosConfig config)
        {
            this.config = config;
        }

        public async Task<ChronosTimeSample> TryGetRemoteUtcNowAsync()
        {
            if (config == null || config.WaterfallProviders == null)
                return ChronosTimeSample.Failed();

            for (int i = 0; i < config.WaterfallProviders.Count; i++)
            {
                DateTimeProvider provider = config.WaterfallProviders[i];
                if (provider == null)
                    continue;

                (bool ok, DateTime dt) = await provider.GetDateTimeUtc();
                if (!ok)
                    continue;

                if (dt.Kind != DateTimeKind.Utc)
                    dt = DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc);

                return new ChronosTimeSample(true, dt, provider.name);
            }

            return ChronosTimeSample.Failed();
        }
    }
}