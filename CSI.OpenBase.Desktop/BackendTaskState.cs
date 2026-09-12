using System.Text.Json;

namespace CSI.OpenBase.Desktop;

internal sealed record BackendTaskItem(
    long Id,
    string Kind,
    string Status,
    string? VideoId,
    string? Message,
    string? CreatedAt,
    string? StartedAt,
    string? FinishedAt)
{
    public bool IsActive => Status is "queued" or "running";
}

internal sealed record BackendTaskState(
    int ActiveTaskCount,
    long? LatestTaskId,
    IReadOnlyList<BackendTaskItem> Tasks)
{
    private const int MaximumTasks = 100;

    public static BackendTaskState Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("本地服务返回了无效的任务状态。");
        }

        var tasks = new List<BackendTaskItem>();
        var taskIds = new HashSet<long>();
        if (root.TryGetProperty("jobs", out var jobs) && jobs.ValueKind == JsonValueKind.Array)
        {
            foreach (var job in jobs.EnumerateArray().Take(MaximumTasks))
            {
                if (job.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = ReadInt64(job, "id");
                if (id is null || id <= 0 || !taskIds.Add(id.Value))
                {
                    continue;
                }

                tasks.Add(new BackendTaskItem(
                    id.Value,
                    ReadText(job, "kind", 64) ?? "task",
                    ReadText(job, "status", 64) ?? "unknown",
                    ReadText(job, "video_id", 64),
                    ReadText(job, "message", 500),
                    ReadText(job, "created_at", 64),
                    ReadText(job, "started_at", 64),
                    ReadText(job, "finished_at", 64)));
            }
        }

        var activeTaskCount = ReadInt32(root, "active_jobs")
            ?? tasks.Count(task => task.IsActive);
        return new BackendTaskState(
            Math.Max(Math.Max(0, activeTaskCount), tasks.Count(task => task.IsActive)),
            ReadInt64(root, "latest_job_id"),
            tasks);
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            return null;
        }

        return result;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
        {
            return null;
        }

        return result;
    }

    private static string? ReadText(
        JsonElement element,
        string propertyName,
        int maximumLength)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
        text = text?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maximumLength
            ? text
            : string.Concat(text.AsSpan(0, maximumLength - 1), "…");
    }
}
