using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using RNOClient.Core.Listeners;
using RNOClient.Core.RenderAPI.Api;
using System;
using System.Collections.Generic;
using System.IO;

namespace RNOClient.Views;

public partial class UploadView : UserControl
{
    private ITaskListener _taskListener;
    private IUIInfluencer _uIInfluencer;
    private String? _selectedPath;
    private Stream? _fileUploadStream;

    public UploadView()
    {
        InitializeComponent();
    }

    public UploadView(ITaskListener taskListener, IUIInfluencer uIInfluencer)
    {
        this._taskListener = taskListener;
        this._uIInfluencer = uIInfluencer;

        InitializeComponent();

        this.BrowseButton.Click += (sender, e) => { 
            BrowseForProject(); 
        };
        this.CancelButton.Click += (sender, e) => { 
            uIInfluencer.ReturnFromUploadDialog();  
        };
        this.UploadButton.Click += (sender, e) => { 
            EnqueueProject(); 
        };
        
    }

    private async void BrowseForProject()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel.StorageProvider != null) Console.WriteLine("Not null!");

        IReadOnlyList<IStorageFile>? files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Project",
            AllowMultiple = false
        });

        if (files.Count == 0) return;

        _selectedPath = files[0].Path.ToString();
        ProjectBox.Text = _selectedPath;

        _fileUploadStream = await files[0].OpenReadAsync();
    }

    private void EnqueueProject()
    {
        if (_selectedPath == null || _fileUploadStream == null) return;

        List<ApiArgType> arguments = new();
        arguments.Add(new ApiArgType("start_frame", this.StartFrameBox.Text.ToString()));
        arguments.Add(new ApiArgType("end_frame", this.EndFrameBox.Text.ToString()));

        String? engineValue = (this.OutputFrameBox.SelectedValue as ComboBoxItem)?.Content?.ToString();
        if (engineValue == null) return;

        arguments.Add(new ApiArgType("output_format", engineValue));

        _taskListener.EnqueueTask(new Core.RenderAPI.Requests.ApiEnqueueRequest(this.EngineBox.SelectedIndex.ToString(), arguments), _fileUploadStream, _selectedPath);

        _uIInfluencer.ReturnFromUploadDialog();
    }
}