using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// 设置 · 推荐页的数据源：内置目录 → 卡片集合。
/// 只负责目录与刷新节奏（TTL 缓存 + 周期后台刷新），下载/安装进度都在卡片里。
/// </summary>
public sealed class RecommendedAppsController : ViewModelBase
{
    /// <summary>后台刷新间隔：保持卡片上的「最新版本」与各仓库发布基本同步。</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(2);

    private readonly IRecommendedAppsService _service;
    private readonly Func<RecommendedAppCardViewModel, Task> _refreshCard;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public RecommendedAppsController(
        IRecommendedAppsService service,
        RecommendedAppsStateStore stateStore,
        Func<RecommendedAppCardViewModel, Task> refreshCard,
        Action<RecommendedAppCardViewModel> installCompleted,
        Action<RecommendedAppCardViewModel, bool> dockMembershipChanged,
        Func<string, string?> toast)
    {
        _service = service;
        _refreshCard = refreshCard;

        foreach (RecommendedApp app in service.GetCatalog())
        {
            var card = new RecommendedAppCardViewModel(
                app, service, stateStore, installCompleted, dockMembershipChanged, toast);
            Cards.Add(card);
        }
    }

    public ObservableCollection<RecommendedAppCardViewModel> Cards { get; } = [];

    /// <summary>设置窗口打开推荐页签时调用：TTL 内走缓存，过期才出网。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            foreach (RecommendedAppCardViewModel card in Cards)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _refreshCard(card).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 页签切换/窗口关闭：下次打开再继续
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>后台周期刷新：静默拉最新发布，让版本徽标保持新鲜。</summary>
    public async Task RunPeriodicRefreshAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                try
                {
                    await RefreshAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppLog.Error("推荐应用周期刷新失败", ex);
                }
            }
        }
        catch
        {
            // PeriodicTimer 释放（进程退出）时结束循环
        }
    }

    /// <summary>Dock 条目变化后同步卡片上的「已加入 Dock」状态（编辑模式移除/导入覆盖）。</summary>
    public void SyncDockMembership(string recommendedId, bool inDock)
    {
        RecommendedAppCardViewModel? card = Cards.FirstOrDefault(c =>
            string.Equals(c.Id, recommendedId, StringComparison.OrdinalIgnoreCase));
        if (card is not null)
        {
            card.Initialize(inDock, card.InstalledRecord);
        }
    }
}
