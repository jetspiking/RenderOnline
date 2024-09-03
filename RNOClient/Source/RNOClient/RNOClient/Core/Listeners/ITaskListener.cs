using RNOClient.Core.RenderAPI.Requests;
using RNOClient.Core.RenderAPI.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RNOClient.Core.Listeners
{
    public interface ITaskListener
    {
        public void DeleteTask(ApiTaskInfo task);
        public void DetailsTask(ApiTaskInfo task);
        public void DownloadTask(ApiTaskInfo task);
        public void EnqueueTask(ApiEnqueueRequest arguments, String filePath);
    }
}
