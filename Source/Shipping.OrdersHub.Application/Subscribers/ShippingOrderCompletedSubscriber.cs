using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shipping.OrdersHub.Domain.Repositories;
using System.Text;

namespace Shipping.OrdersHub.Application.Subscribers;

public class ShippingOrderCompletedSubscriber : BackgroundService
{
    private readonly IModel _channel;
    private const string Queue = "shipping-orders-service/shipping-order-completed";
    private const string RoutingKeySubscribe = "shipping-order-completed";
    private readonly IServiceProvider _serviceProvider;
    private const string TrackingsExchange = "trackings-service";

    public ShippingOrderCompletedSubscriber(IServiceProvider serviceProvider)
    {
        var connectionFactory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "guest",
            Password = "guest"
        };

        var connection = connectionFactory.CreateConnection("shipping-order-completed-consumer");

        _channel = connection.CreateModel();

        _channel.QueueDeclare(
            queue: Queue,
            durable: true,
            exclusive: false,
            autoDelete: false);

        _channel.QueueBind(Queue, TrackingsExchange, RoutingKeySubscribe);

        _serviceProvider = serviceProvider;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += (sender, eventArgs) =>
        {
            var contentArray = eventArgs.Body.ToArray(); 
            var contentString = Encoding.UTF8.GetString(contentArray);
            var @event = JsonConvert.DeserializeObject<ShippingOrderIsCompletedEvent>(contentString);

            Console.WriteLine($"Message ShippingOrderIsCompletedEvent received with Code {@event!.TrackingCode}");

            Complete(@event).Wait(stoppingToken);

            _channel.BasicAck(eventArgs.DeliveryTag, false);
        };

        _channel.BasicConsume(Queue, false, consumer);

        return Task.CompletedTask;
    }

    private async Task Complete(ShippingOrderIsCompletedEvent @event)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IShippingOrderRepository>();
        var shippingOrder = await repository.GetByCodeAsync(@event.TrackingCode!);
        shippingOrder.SetCompleted();
        await repository.UpdateAsync(shippingOrder);
    }
}

public class ShippingOrderIsCompletedEvent
{
    public string? TrackingCode { get; set; }
}