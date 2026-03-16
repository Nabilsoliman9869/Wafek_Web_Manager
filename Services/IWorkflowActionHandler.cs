namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// معالج إجراء وورك فلو — رفع السرية، تشغيل بروسيجر، تغيير المرحلة، إلخ
    /// </summary>
    public interface IWorkflowActionHandler
    {
        string ActionType { get; }
        Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct = default);
    }

    public class WorkflowActionContext
    {
        public long LogId { get; set; }
        public int WorkflowId { get; set; }
        public int StepOrder { get; set; }
        public Guid SourceRecordId { get; set; }
        public string SourceTable { get; set; } = "";
        public string ResponseType { get; set; } = "";  // Approved, Rejected, Postponed
        public string SelectedValue { get; set; } = "";
        public Dictionary<string, string> Params { get; set; } = new();
        public string ConnectionString { get; set; } = "";
    }
}
