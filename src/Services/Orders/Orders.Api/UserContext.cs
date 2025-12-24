using Microsoft.AspNetCore.Http;

namespace Orders.Api;

public static class UserContext
{
    public const string HeaderName = "X-User-Id";
    private const string ItemKey = "user_id";

    public static IApplicationBuilder UseUserId(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var userId = ctx.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrWhiteSpace(userId))
            {
                userId = ctx.Request.Query["user_id"].ToString();
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                ctx.Items[ItemKey] = userId;
            }

            await next();
        });
    }

    public static string GetRequiredUserId(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(ItemKey, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        throw new BadHttpRequestException($"Missing {HeaderName} header (or user_id query param).");
    }
}
