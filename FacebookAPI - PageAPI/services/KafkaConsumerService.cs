using Confluent.Kafka;
using System.Text.Json;

namespace FacebookAPI___PageAPI.Services
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly IConsumer<Ignore, string> _consumer;
        private readonly string _topic;
        private readonly IProducer<string, string> _producer;
        private readonly string _failedTopic;

        public KafkaConsumerService(
            ILogger<KafkaConsumerService> logger,
            IConfiguration configuration)
        {
            _logger = logger;

            var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
            _topic = configuration["Kafka:Topic"] ?? "raw-events";
            _failedTopic = configuration["Kafka:FailedTopic"] ?? "send-failed";

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = configuration["Kafka:GroupId"] ?? "core-service-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true
            };

            _consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrapServers
            };

            _producer = new ProducerBuilder<string, string>(producerConfig).Build();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Kafka Consumer Service started.");

            _consumer.Subscribe(_topic);

            _logger.LogInformation("Subscribed to Kafka topic: {Topic}", _topic);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = _consumer.Consume(stoppingToken);

                        if (consumeResult == null)
                        {
                            continue;
                        }

                        var messageValue = consumeResult.Message.Value;

                        _logger.LogInformation("=======================================");
                        _logger.LogInformation("Received raw event from Kafka:");
                        _logger.LogInformation("{MessageValue}", messageValue);

                        await ProcessEventAsync(messageValue);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Error consuming message: {Reason}", ex.Error.Reason);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Kafka Consumer Service is stopping.");
            }
            finally
            {
                _consumer.Close();
                _consumer.Dispose();

                _producer.Flush(TimeSpan.FromSeconds(5));
                _producer.Dispose();
            }
        }

        private async Task ProcessEventAsync(string rawEvent)
        {
            try
            {
                _logger.LogInformation("Event status: received");

                var messageText = ExtractMessage(rawEvent);

                _logger.LogInformation("Extracted message: {MessageText}", messageText);

                if (IsSpam(messageText))
                {
                    _logger.LogWarning("Spam detected: {MessageText}", messageText);
                    _logger.LogInformation("Auto-decision: Ẩn bình luận hoặc chuyển sang hàng chờ kiểm duyệt.");
                    _logger.LogInformation("Event status: processed_spam");
                    return;
                }

                if (rawEvent.Contains("FORCE_ERROR"))
                {
                    throw new Exception("Simulated processing error for testing retry flow.");
                }

                var analyzedData = await AnalyzeWithAIAsync(messageText);

                _logger.LogInformation(
                    "AI Analysis Result: Intent={Intent}, Sentiment={Sentiment}",
                    analyzedData.Intent,
                    analyzedData.Sentiment
                );

                if (analyzedData.Intent == "khiếu nại" && analyzedData.Sentiment == "tiêu cực")
                {
                    _logger.LogWarning("Auto-decision: Cần phản hồi khẩn cấp hoặc tạo ticket hỗ trợ.");
                }
                else if (analyzedData.Intent == "hỏi giá")
                {
                    _logger.LogInformation("Auto-decision: Tự động phản hồi thông tin giá.");
                }
                else
                {
                    _logger.LogInformation("Auto-decision: Ghi nhận tương tác bình thường.");
                }

                _logger.LogInformation("Event status: processed");
                _logger.LogInformation("Event processed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event. Should publish to send_failed topic for retry.");
                _logger.LogInformation("Event status: failed");
            }
        }

        private string ExtractMessage(string rawEvent)
        {
            try
            {
                using var document = JsonDocument.Parse(rawEvent);
                var root = document.RootElement;

                if (root.TryGetProperty("message", out var messageProperty))
                {
                    return messageProperty.GetString() ?? string.Empty;
                }

                return rawEvent;
            }
            catch
            {
                return rawEvent;
            }
        }

        private bool IsSpam(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var lowerMessage = message.ToLower();

            if (lowerMessage.Contains("http://")) return true;
            if (lowerMessage.Contains("https://")) return true;
            if (lowerMessage.Contains("spam")) return true;
            if (lowerMessage.Contains("khuyến mãi sốc")) return true;

            return false;
        }

        private async Task<(string Intent, string Sentiment)> AnalyzeWithAIAsync(string text)
        {
            await Task.Delay(100);

            var lowerText = text.ToLower();

            if (lowerText.Contains("giá") || lowerText.Contains("bao nhiêu"))
            {
                return ("hỏi giá", "trung tính");
            }

            if (lowerText.Contains("lỗi") || lowerText.Contains("tệ") || lowerText.Contains("không hài lòng"))
            {
                return ("khiếu nại", "tiêu cực");
            }

            if (lowerText.Contains("hay") || lowerText.Contains("tốt") || lowerText.Contains("thích"))
            {
                return ("tương tác", "tích cực");
            }

            return ("tương tác", "trung tính");
        }
    }

    private async Task PublishFailedEventAsync(string rawEvent, Exception exception)
        {
            var failedEvent = new
            {
                failedId = Guid.NewGuid().ToString(),
                sourceTopic = _topic,
                errorType = exception.GetType().Name,
                errorMessage = exception.Message,
                failedAt = DateTime.UtcNow,
                retryCount = 0,
                status = "failed",
                rawEvent = rawEvent
            };

            var failedJson = JsonSerializer.Serialize(failedEvent);

            await _producer.ProduceAsync(_failedTopic, new Message<string, string>
            {
                Key = failedEvent.failedId,
                Value = failedJson
            });

            _logger.LogWarning("Published failed event to Kafka topic: {FailedTopic}", _failedTopic);
            _logger.LogWarning("Failed event payload: {FailedJson}", failedJson);
        }
    }
}