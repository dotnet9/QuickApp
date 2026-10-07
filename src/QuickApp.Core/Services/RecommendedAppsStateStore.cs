using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuickApp.Core.Json;

namespace QuickApp.Core.Services;

/// <summary>一个推荐应用的本地安装记录。版本用于跟最新发布比较，提示可更新。</summary>
public sealed class RecommendedInstallRecord
{
    public string AppId { get; set; } = string.Empty;

    /// <summary>安装时的发布 tag（如 v1.5.1）。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>安装后探测到的主程序路径；未探测到时为空（加入 Dock 时再探测）。</summary>
    public string? LauncherPath { get; set; }

    public string? InstalledAt { get; set; }
}

public sealed class RecommendedAppsState
{
    public List<RecommendedInstallRecord> Installed { get; set; } = [];
}

/// <summary>
/// 推荐应用的安装状态持久化（配置目录下 recommended-state.json）。
/// 独立于 Dock 条目：用户把快捷方式从 Dock 移除后，「已安装」的状态仍然保留，
/// 推荐页才能给出「加入 Dock」而不是重新下载。
/// </summary>
public sealed class RecommendedAppsStateStore
{
    private readonly string _stateFile;
    private readonly Action<string>? _log;

    public RecommendedAppsStateStore(string baseDirectory, Action<string>? log = null)
    {
        _stateFile = AppPaths.RecommendedStateFile(baseDirectory);
        _log = log;
    }

    public string StateFile => _stateFile;

    public RecommendedAppsState Load()
    {
        try
        {
            if (!File.Exists(_stateFile))
            {
                return new RecommendedAppsState();
            }

            string text = File.ReadAllText(_stateFile);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new RecommendedAppsState();
            }

            RecommendedAppsState? state = JsonSerializer.Deserialize(text, AppJsonContext.Default.RecommendedAppsState);
            return state ?? new RecommendedAppsState();
        }
        catch (Exception ex)
        {
            _log?.Invoke("读取推荐安装状态失败：" + ex.Message);
            return new RecommendedAppsState();
        }
    }

    public RecommendedInstallRecord? Find(string appId)
        => Load().Installed.FirstOrDefault(record =>
            string.Equals(record.AppId, appId, StringComparison.OrdinalIgnoreCase));

    public void Upsert(RecommendedInstallRecord record)
    {
        RecommendedAppsState state = Load();
        RecommendedInstallRecord? existing = state.Installed.FirstOrDefault(entry =>
            string.Equals(entry.AppId, record.AppId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            int index = state.Installed.IndexOf(existing);
            state.Installed[index] = record;
        }
        else
        {
            state.Installed.Add(record);
        }

        Save(state);
    }

    public void Remove(string appId)
    {
        RecommendedAppsState state = Load();
        int removed = state.Installed.RemoveAll(entry =>
            string.Equals(entry.AppId, appId, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Save(state);
        }
    }

    private void Save(RecommendedAppsState state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_stateFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_stateFile, JsonSerializer.Serialize(state, AppJsonContext.Default.RecommendedAppsState));
        }
        catch (Exception ex)
        {
            _log?.Invoke("保存推荐安装状态失败：" + ex.Message);
        }
    }
}
