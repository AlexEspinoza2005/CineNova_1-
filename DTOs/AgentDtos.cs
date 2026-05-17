namespace MovieApi.DTOs
{
    public class AgentQueryRequest
    {
        public string Question { get; set; } = string.Empty;
    }

    public class AgentQueryResponse
    {
        public string Answer { get; set; } = string.Empty;
        public object? Data { get; set; }
        public int LatencyMs { get; set; }
    }
}
