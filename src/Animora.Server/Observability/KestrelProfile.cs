using Microsoft.AspNetCore.Server.Kestrel.Core;
namespace Animora.Server.Observability;

internal static class KestrelProfile
{
    public static void Apply(KestrelServerOptions options,int profileGiB)
    {
        if(profileGiB is not (2 or 4)) throw new ArgumentOutOfRangeException(nameof(profileGiB));
        var limits=options.Limits; limits.MaxConcurrentConnections=profileGiB==2?256:512;
        limits.MaxConcurrentUpgradedConnections=64; limits.MaxRequestBodySize=2L*1024*1024;
        limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15); limits.KeepAliveTimeout=TimeSpan.FromSeconds(60);
        limits.MinRequestBodyDataRate=new MinDataRate(240,TimeSpan.FromSeconds(10));
    }
}
