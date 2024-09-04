using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json;
using RNOClient.Core.RenderAPI.Responses;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using RNOClient.Core.Listeners;
using MsBox.Avalonia.Base;
using RNOClient.Core.RenderAPI.Requests;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using Avalonia.Media;
using System.Runtime.CompilerServices;
using System.Text;

namespace RNOClient.Views
{
    public partial class MainView : UserControl, ITaskListener, IUIInfluencer
    {
        private String _ipAddress = "127.0.0.1";
        private String _port = "5001";

        private HttpClientHandler _httpClientHandler;

        public MainView()
        {
            InitializeComponent();

            this._httpClientHandler = new();

#if DEBUG
            try
            {
                this._httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    return true;
                };
            }
            catch (Exception e)
            {
                Label responseLabel = new()
                {
                    Content = e.Message
                };
                this.Content = responseLabel;
            }
#endif

            TaskView labelTaskView = new(null, this as ITaskListener);
            TasksPanel.Children.Add(labelTaskView);

            this.ValidateButton.PointerPressed += (sender, e) => RenderAPIInfoRequest();

            this.UploadButton.PointerPressed += (sender, e) =>
            {
                this.UploadViewer.Content = new UploadView(this as ITaskListener, this as IUIInfluencer);
                TasksViewer.IsVisible = false;
                UploadViewer.IsVisible = true;
            };
        }

        private async void RenderAPIInfoRequest()
        {
            TasksPanel.Children.Clear();

            TaskView labelTaskView = new(null, this as ITaskListener);
            TasksPanel.Children.Add(labelTaskView);

            HttpClient httpClient = new HttpClient(this._httpClientHandler);

            httpClient.DefaultRequestHeaders.Add("email", this.EmailBox.Text);
            httpClient.DefaultRequestHeaders.Add("token", this.TokenBox.Text);
            httpClient.BaseAddress = new Uri($"https://{_ipAddress}:{_port}");

            try
            {
                HttpResponseMessage statusResponse = await httpClient.GetAsync("/renderapi/v1/info");

                if (statusResponse.IsSuccessStatusCode)
                {

                    String responseBody = await statusResponse.Content.ReadAsStringAsync();
                    ApiInfoResponse? apiInfoResponse = JsonConvert.DeserializeObject<ApiInfoResponse>(responseBody);

                    if (apiInfoResponse == null) return;

                    this.TitleLabel.Content = $"{apiInfoResponse.User.FirstName} {apiInfoResponse.User.LastName}";

                    int queueCount = 0;
                    foreach (ApiTaskInfo apiTaskInfo in apiInfoResponse.Tasks)
                    {
                        TasksPanel.Children.Add(new TaskView(apiTaskInfo, this as ITaskListener));
                        if (apiTaskInfo.Task.EndTime == null) queueCount++;
                    }

                    this.TasksCountLabel.Content = apiInfoResponse.Tasks.Count.ToString();
                    this.QueueCountLabel.Content = queueCount.ToString();
                }
                else
                {
                    if (statusResponse.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard($"Error", $"Invalid credentials.", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterOwner);
                        ButtonResult result = await box.ShowAsync();
                    }

#if DEBUG
                    Label responseLabel = new()
                    {
                        Content = statusResponse
                    };
                    this.Content = responseLabel;
#endif
                }
            }
            catch (HttpRequestException e)
            {
                IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard($"Error", $"Failed to retrieve tasks.", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterOwner);
                ButtonResult result = await box.ShowAsync();

#if DEBUG
                Label responseLabel = new()
                {
                    Content = e.Message
                };
                this.Content = responseLabel;
#endif
            }
        }

        private async Task RenderAPIDeleteRequest(ApiTaskInfo apiTaskInfo)
        {
            HttpClient httpClient = new HttpClient(this._httpClientHandler);

            httpClient.DefaultRequestHeaders.Add("email", this.EmailBox.Text);
            httpClient.DefaultRequestHeaders.Add("token", this.TokenBox.Text);
            httpClient.BaseAddress = new Uri($"https://{_ipAddress}:{_port}");

            try
            {
                HttpResponseMessage statusResponse = await httpClient.PostAsJsonAsync("/renderapi/v1/delete", new ApiDeleteRequest(apiTaskInfo.Task.TaskId));

                if (statusResponse.IsSuccessStatusCode)
                {

                    String responseBody = await statusResponse.Content.ReadAsStringAsync();
                    ApiDeleteResponse? apiDeleteResponse = JsonConvert.DeserializeObject<ApiDeleteResponse>(responseBody);

                    if (apiDeleteResponse == null) return;

                    IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard($"Info", $"Deleted \"{apiTaskInfo.Render?.FileName}\".", ButtonEnum.Ok, Icon.Success, WindowStartupLocation.CenterOwner);
                    ButtonResult result = await box.ShowAsync();
                }
                else
                {
                    IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard($"Error", $"Failed to delete \"{apiTaskInfo.Render?.FileName}\".", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterOwner);
                    ButtonResult result = await box.ShowAsync();

#if DEBUG
                    Label responseLabel = new()
                    {
                        Content = statusResponse
                    };
                    this.Content = responseLabel;
#endif
                }
            }
            catch (HttpRequestException e)
            {
#if DEBUG
                Label responseLabel = new()
                {
                    Content = e.Message
                };
                this.Content = responseLabel;
#endif
            }
        }

        private async Task RenderAPIDownloadRequest(ApiTaskInfo apiTaskInfo)
        {
            HttpClient httpClient = new HttpClient(this._httpClientHandler);

            httpClient.DefaultRequestHeaders.Add("email", this.EmailBox.Text);
            httpClient.DefaultRequestHeaders.Add("token", this.TokenBox.Text);
            httpClient.BaseAddress = new Uri($"https://{_ipAddress}:{_port}");

            try
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/renderapi/v1/download")
                {
                    Content = JsonContent.Create(new ApiDownloadRequest(apiTaskInfo.Task.TaskId))
                };

                HttpResponseMessage statusResponse = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (statusResponse.IsSuccessStatusCode)
                {
                    Byte[] fileData = await statusResponse.Content.ReadAsByteArrayAsync();

                    ContentDispositionHeaderValue contentDisposition = statusResponse.Content.Headers.ContentDisposition;
                    string fileName = contentDisposition != null ? contentDisposition.FileName.Trim('"') : "download.zip";

                    await SaveFileUsingStorageProvider(fileData, fileName);
                }
                else
                {
                    IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard($"Error", $"Failed to download \"{apiTaskInfo.Render?.FileName}\".", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterOwner);
                    ButtonResult result = await box.ShowAsync();

#if DEBUG
                    Label responseLabel = new Label
                    {
                        Content = statusResponse.ToString()
                    };
                    this.Content = responseLabel;
#endif
                }
            }
            catch (HttpRequestException e)
            {
#if DEBUG
                Label responseLabel = new Label
                {
                    Content = e.Message
                };
                this.Content = responseLabel;
#endif
            }
        }

        private async Task RenderAPIEnqueueRequest(ApiEnqueueRequest task, String filePath)
        {
            HttpClient httpClient = new HttpClient(this._httpClientHandler);
            httpClient.DefaultRequestHeaders.Add("email", this.EmailBox.Text);
            httpClient.DefaultRequestHeaders.Add("token", this.TokenBox.Text);
            httpClient.BaseAddress = new Uri($"https://{_ipAddress}:{_port}");

            MultipartFormDataContent content = new MultipartFormDataContent();
            FileStream? fileStream = null;
            StreamContent? fileContent = null;

            try
            {
                // Serialize the task to JSON and add it as a string content
                String jsonTask = JsonConvert.SerializeObject(task);
                StringContent jsonContent = new StringContent(jsonTask, Encoding.UTF8, "application/json");

                // Add content-disposition header manually for the request JSON part
                jsonContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"request\""
                };

                content.Add(jsonContent);

                // Add the file content
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // Add content-disposition header manually for the file part
                fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"file\"",
                    FileName = $"\"{Path.GetFileName(filePath)}\""
                };

                content.Add(fileContent);

                // Send the request
                HttpResponseMessage statusResponse = await httpClient.PostAsync("/renderapi/v1/enqueue", content);
                
                String responseBody = await statusResponse.Content.ReadAsStringAsync();
                ApiEnqueueResponse? apiEnqueueResponse = JsonConvert.DeserializeObject<ApiEnqueueResponse>(responseBody);

                if (statusResponse.IsSuccessStatusCode)
                {
                    IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard("Info", $"Enqueued \"{Path.GetFileName(filePath)}\".", ButtonEnum.Ok, Icon.Success, WindowStartupLocation.CenterOwner);
                    ButtonResult result = await box.ShowAsync();
                }
                else
                {
#if DEBUG
                    Label responseLabel = new Label
                    {
                        Content = $"Status: {statusResponse.StatusCode}\nReason: {statusResponse.ReasonPhrase}\nContent: {responseBody}"
                    };
                    this.Content = responseLabel;
#endif

                    IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to enqueue: {statusResponse.ReasonPhrase}", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterOwner);
                    ButtonResult result = await box.ShowAsync();
                }
            }
            catch (Exception e)
            {
#if DEBUG
                Label responseLabel = new Label
                {
                    Content = e.Message
                };
                this.Content = responseLabel;
#endif
            }
        }

        private async Task SaveFileUsingStorageProvider(byte[] fileData, string fileName)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            IStorageFile? fileResult = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = fileName,
                Title = "Save Result",
                FileTypeChoices = new List<FilePickerFileType> {
                    new FilePickerFileType("ZIP files") { Patterns = new[] { "*.zip" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });

            if (fileResult != null)
            {
                await using (Stream stream = await fileResult.OpenWriteAsync())
                {
                    await stream.WriteAsync(fileData, 0, fileData.Length);
                }
            }
        }


        public async void DeleteTask(ApiTaskInfo task)
        {
            IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard($"Delete", $"Delete \"{task.Render?.FileName}\"?", ButtonEnum.YesNo, Icon.Question, WindowStartupLocation.CenterOwner);
            ButtonResult result = await box.ShowAsync();
            if (result == ButtonResult.Yes)
            {
                await RenderAPIDeleteRequest(task);
                RenderAPIInfoRequest();
            }
        }

        public void DetailsTask(ApiTaskInfo task)
        {
            throw new NotImplementedException();
        }

        public async void DownloadTask(ApiTaskInfo task)
        {
            await RenderAPIDownloadRequest(task);
        }

        public async void EnqueueTask(ApiEnqueueRequest task, String filePath)
        {
            await RenderAPIEnqueueRequest(task, filePath);
            RenderAPIInfoRequest();
        }

        public void ReturnFromUploadDialog()
        {
            TasksViewer.IsVisible = true;
            UploadViewer.IsVisible = false;
        }
    }
}