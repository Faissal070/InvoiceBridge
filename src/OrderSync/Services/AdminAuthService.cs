using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OrderSync.Options;

namespace OrderSync.Services;

/// <summary>
/// Validates admin cookies. The cookie value is HMAC-SHA256(key=AdminCode, data="ordersync-admin").
/// </summary>
public sealed class AdminAuthService(IOptions<AdminOptions> options)
{
    private readonly AdminOptions _options = options.Value;

    public bool IsAdmin(HttpContext? ctx)
    {
        if (ctx is null || string.IsNullOrEmpty(_options.AdminCode))
            return false;
        if (!ctx.Request.Cookies.TryGetValue("admin_token", out var cookie) || string.IsNullOrEmpty(cookie))
            return false;
        try
        {
            var expected = ComputeToken(_options.AdminCode);
            var expectedBytes = Convert.FromHexString(expected);
            var actualBytes = Convert.FromHexString(cookie);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeToken(string adminCode)
    {
        var key = Encoding.UTF8.GetBytes(adminCode);
        var data = Encoding.UTF8.GetBytes("ordersync-admin");
        return Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }
}
