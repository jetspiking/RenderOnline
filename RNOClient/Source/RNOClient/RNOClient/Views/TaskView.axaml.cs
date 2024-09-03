using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using RNOClient.Core.Listeners;
using RNOClient.Core.RenderAPI.Responses;

namespace RNOClient.Views;

public partial class TaskView : UserControl
{
    private ITaskListener? _taskListener;
    public ApiTaskInfo? ApiTaskInfo { get; set; }

    public TaskView()
    {
        InitializeComponent();
    }

    public TaskView(ApiTaskInfo? taskInfo, ITaskListener taskListener)
    {
        InitializeComponent();

        this._taskListener = taskListener;
        this.ApiTaskInfo = taskInfo;

        if (taskInfo == null)
        {
            this.StatusCheckImage.Source = null;
            this.StatusBusyImage.IsVisible = false;
            this.StatusErrorImage.IsVisible = false;
            this.DownloadButton.Source = null;
            this.TrashButton.Source = null;
            this.TrashButton.IsEnabled = false;
            return;
        }

        this.Margin = new Thickness(5, 5, 5, 5);
        this.Background = new SolidColorBrush(Colors.WhiteSmoke);

        if (taskInfo.Task.IsSuccess)
        {
            this.StatusCheckImage.IsVisible = true;
            this.StatusBusyImage.IsVisible = false;
            this.StatusErrorImage.IsVisible = false;
            this.DownloadButton.IsVisible = true;
        }
        if (taskInfo.Task.IsRunning || taskInfo.Task.EndTime == null)
        {
            this.StatusBusyImage.IsVisible = true;
            this.StatusCheckImage.IsVisible = false;
            this.StatusErrorImage.IsVisible = false;
            this.DownloadButton.Source = null;
        }
        if (!taskInfo.Task.IsSuccess && !taskInfo.Task.IsRunning && taskInfo.Task.EndTime != null)
        {
            this.StatusCheckImage.IsVisible = false;
            this.StatusErrorImage.IsVisible = true;
            this.StatusBusyImage.IsVisible = false;
            this.DownloadButton.Source = null;
        }
        if (!taskInfo.Task.IsSuccess && !taskInfo.Task.IsRunning && taskInfo.Task.EndTime == null)
        {
            this.StatusBusyImage.IsVisible = true;
            this.StatusCheckImage.IsVisible = false;
            this.StatusErrorImage.IsVisible = false;
            this.DownloadButton.Source = null;
        }

        this.FileName.Content = taskInfo?.Render?.FileName;
        this.FileSize.Content = taskInfo?.Render?.FileSize;
        this.Engine.Content = taskInfo?.Engine?.Name;
        this.QueueTime.Content = taskInfo?.Task.QueueTime.ToString();
        this.StartTime.Content = taskInfo?.Task.StartTime.ToString();
        this.StopTime.Content = taskInfo?.Task.EndTime.ToString();

        TrashButton.PointerPressed += (sender, e) =>
        {
            _taskListener.DeleteTask(taskInfo);
        };

        DownloadButton.PointerPressed += (sender, e) =>
        {
            _taskListener.DownloadTask(taskInfo);
        };
    }
}