using System.Text.Json.Serialization;
using QuickApp.Core.Models;

namespace QuickApp.Core.Json;

/// <summary>
/// System.Text.Json 源生成上下文：NativeAOT 下必须走源生成，反射式序列化会被裁剪掉。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LauncherItem))]
[JsonSerializable(typeof(QuickApp.Core.Services.GitHubRelease))]
[JsonSerializable(typeof(QuickApp.Core.Services.GitHubAsset))]
[JsonSerializable(typeof(QuickApp.Core.Services.UpdateCheckState))]
[JsonSerializable(typeof(RecommendedApp))]
[JsonSerializable(typeof(System.Collections.Generic.List<RecommendedApp>))]
[JsonSerializable(typeof(QuickApp.Core.Services.RecommendedAppsState))]
public sealed partial class AppJsonContext : JsonSerializerContext
{
}
