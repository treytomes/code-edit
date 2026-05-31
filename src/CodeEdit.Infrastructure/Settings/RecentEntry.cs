using System.Text.Json.Serialization;

namespace CodeEdit.Infrastructure.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecentKind { File, Folder }

public sealed record RecentEntry(string Path, RecentKind Kind);
