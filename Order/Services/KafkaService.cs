using Confluent.Kafka;

using System.Text.Json;

namespace OrderService.Services
{
    public interface IKafkaProducer
    {
        Task ProduceAsync<T>(string topic, T message);
    }

    public class KafkaProducer : IKafkaProducer
    {
        private readonly IProducer<Null, string> _producer;
        private readonly ILogger<KafkaProducer> _logger;

        public KafkaProducer(IConfiguration configuration, ILogger<KafkaProducer> logger)
        {
            var config = new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"]
            };
            _producer = new ProducerBuilder<Null, string>(config).Build();
            _logger = logger;
        }

        public async Task ProduceAsync<T>(string topic, T message)
        {
            try
            {
                var messageJson = JsonSerializer.Serialize(message);
                await _producer.ProduceAsync(topic, new Message<Null, string> { Value = messageJson });
                _logger.LogInformation($"Message sent to topic {topic}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error producing message to topic {topic}");
                throw;
            }
        }
    }

    public interface IKafkaConsumer<T>
    {
        void Subscribe(string topic, Action<T> messageHandler);
    }

    public class KafkaConsumer<T> : IKafkaConsumer<T>, IDisposable
    {
        private readonly IConsumer<Ignore, string> _consumer;
        private readonly ILogger<KafkaConsumer<T>> _logger;

        public KafkaConsumer(IConfiguration configuration, ILogger<KafkaConsumer<T>> logger)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                GroupId = $"flowers-{typeof(T).Name}-group",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };
            _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            _logger = logger;
        }

        public void Subscribe(string topic, Action<T> messageHandler)
        {
            _consumer.Subscribe(topic);

            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        var consumeResult = _consumer.Consume();
                        var message = JsonSerializer.Deserialize<T>(consumeResult.Message.Value);
                        if (message != null)
                        {
                            messageHandler(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error consuming message");
                    }
                }
            });
        }

        public void Dispose()
        {
            _consumer?.Close();
            _consumer?.Dispose();
        }
    }
}