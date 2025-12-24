namespace Orders.Api.Messaging;

public sealed class RabbitOptions
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}
