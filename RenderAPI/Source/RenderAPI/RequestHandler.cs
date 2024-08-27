using Microsoft.AspNetCore.Mvc.RazorPages;
using RenderAPI.Core;
using RenderAPI.Core.Configuration;
using RenderAPI.Misc;
using System.Diagnostics;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Data;
using System.Net.Http;
using RenderAPI.Core.Database;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Identity.Data;
using RenderAPI.Core.Responses;
using RenderAPI.Core.Api;
using Microsoft.AspNetCore.Mvc.Formatters;
using RenderAPI.Core.Requests;
using System.Text.RegularExpressions;
using System.Reflection.PortableExecutable;
using RenderAPI.HPCServer;
using Mysqlx.Session;
using Org.BouncyCastle.Crypto.IO;

namespace RenderAPI
{
    public class RequestHandler
    {
        private Core.Configuration.RenderServer _renderApiConfiguration;

        private MySqlConnection _databaseMySqlConnection;
        private HttpClientHandler _httpClientHandler;

        public RequestHandler(WebApplication app)
        {
            const String configurationFileName = "Configuration.json";
            String configurationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configurationFileName);

            this._httpClientHandler = new();
            this._httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                return true;
            };

            Console.WriteLine("Searching for configuration file: " + configurationFilePath);

            Core.Configuration.RenderServer? configuration = JsonManager.DeserializeFromFile<Core.Configuration.RenderServer>(configurationFilePath);

            if (configuration == null)
            {
                Console.WriteLine($"{configurationFileName} not found!");
                Console.ReadKey();
                Environment.Exit(0);
            }

            this._renderApiConfiguration = configuration;

            if (!InitializeDatabaseConnection() || this._databaseMySqlConnection == null)
            {
                Console.WriteLine("Failed to connect to the database. Exiting...");
                Console.ReadKey();
                Environment.Exit(1);
            }

            app.Urls.Add("https://localhost:" + _renderApiConfiguration.Port);
            app.UseHttpsRedirection();
            app.UseAuthorization();

            // Get machine status.
            app.MapGet("/renderapi/v1/info", HandleInfoRequest);

            // Queue a rendering assignment.
            app.MapPost("/renderapi/v1/enqueue", HandleEnqueueRequest);

            // Remove a rendering assignment.
            app.MapPost("/renderapi/v1/dequeue", HandleDequeueRequest);

            // Download a rendering result.
            app.MapPost("/renderapi/v1/download", HandleDownloadRequest);

            // Start the polling service to monitor render tasks.
            StartPollingService();

            app.Run();
        }

        private Boolean InitializeDatabaseConnection()
        {
            try
            {
                _databaseMySqlConnection = new(_renderApiConfiguration.ConnectionString);
                _databaseMySqlConnection.Open();

                Console.WriteLine("Database connection successful!");
                return true;

            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Database connection error: {ex.Message}");
                return false;
            }
        }

        private ApiUser? AuthenticateAndGetUserInDatabase(HttpContext httpContext)
        {
            String? emailHeader = httpContext.Request.Headers["email"];
            String? tokenHeader = httpContext.Request.Headers["token"];

            if (String.IsNullOrEmpty(emailHeader) || String.IsNullOrEmpty(tokenHeader))
            {
                return null;
            }

            String query = @"
                        SELECT 
                            u.user_id,
                            u.first_name,
                            u.last_name,
                            u.email,
                            u.subscription_id,
                            u.is_active,
                            u.token
                        FROM 
                            users u 
                        WHERE 
                            u.email = @Email AND token = @Token AND u.is_active = 1";

            MySqlCommand command = new MySqlCommand(query, this._databaseMySqlConnection);
            command.Parameters.AddWithValue("@Email", emailHeader);
            command.Parameters.AddWithValue("@Token", tokenHeader);

            MySqlDataReader reader = command.ExecuteReader();

            DbUser? dbUser = null;

            if (reader.Read())
            {
                UInt16 userId = reader.GetUInt16("user_id");
                String firstName = reader.GetString("first_name");
                String lastName = reader.GetString("last_name");
                String email = reader.GetString("email");
                Byte subscriptionId = reader.GetByte("subscription_id");
                Boolean isActive = reader.GetBoolean("is_active");
                String token = reader.GetString("token");

                dbUser = new DbUser(userId, firstName, lastName, email, subscriptionId, isActive, token);
            }

            reader.Close();

            if (dbUser == null) return null;

            return new ApiUser(dbUser.UserId, dbUser.FirstName, dbUser.LastName, dbUser.Email, dbUser.SubscriptionId, dbUser.IsActive);
        }

        public async Task HandleInfoRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            String retrieveTaskQuery = @"
                        SELECT 
                            t.task_id, 
                            t.user_id,
                            t.queue_time,
                            t.start_time, 
                            t.end_time, 
                            t.is_running, 
                            t.is_success,
                            t.render_id,
                            t.machine_id,
                            r.render_id,
                            r.file_name,
                            r.file_path,
                            r.file_size,
                            r.arguments,
                            r.engine_id,
                            e.engine_id,
                            e.name,
                            e.extension,
                            e.download_path,
                            e.render_argument
                        FROM 
                            tasks t
                        LEFT OUTER JOIN 
                            queue q ON t.task_id = q.task_id
                        LEFT OUTER JOIN 
                            renders r ON t.render_id = r.render_id
                        LEFT OUTER JOIN
                            engines e ON r.engine_id = e.engine_id
                        WHERE 
                            t.user_id = @UserId";

            MySqlCommand command = new MySqlCommand(retrieveTaskQuery, this._databaseMySqlConnection);
            command.Parameters.AddWithValue("@UserId", user.UserId);

            MySqlDataReader reader = command.ExecuteReader();

            List<ApiTaskInfo> tasks = new();

            while (reader.Read())
            {
                // Read task details
                UInt64 taskId = reader.GetUInt64("task_id");
                UInt16 userId = reader.GetUInt16("user_id");
                DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                Boolean isRunning = reader.GetBoolean("is_running");
                Boolean isSuccess = reader.GetBoolean("is_success");
                UInt64 renderId = reader.GetUInt64("render_id");
                Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                // Read render details
                String fileName = reader.GetString("file_name");
                String filePath = reader.GetString("file_path");
                UInt64 fileSize = reader.GetUInt64("file_size");
                String arguments = reader.GetString("arguments");
                Byte engineId = reader.GetByte("engine_id");

                DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                // Read engine details
                String engineName = reader.GetString("name");
                String extension = reader.GetString("extension");
                String downloadPath = reader.GetString("download_path");
                String renderArgument = reader.GetString("render_argument");

                DbEngine dbEngine = new(engineId, engineName, extension, downloadPath, renderArgument);

                ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);
                ApiEngine apiEngine = new(dbEngine.EngineId, dbEngine.Name, dbEngine.Extension, dbEngine.DownloadPath, dbEngine.RenderArgument);

                tasks.Add(new ApiTaskInfo(apiTask, apiRender, apiEngine));
            }

            reader.Close();

            ApiInfoResponse infoResponse = new(user, tasks);

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(infoResponse));
        }

        public async Task HandleEnqueueRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Unauthorized.")));
                return;
            }

            if (!httpContext.Request.HasFormContentType || !httpContext.Request.Form.Files.Any())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "No files were uploaded.")));
                return;
            }

            IFormCollection form = await httpContext.Request.ReadFormAsync();
            String? jsonRequestString = form["request"];

            if (jsonRequestString == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Header \"request\" is empty.")));
                return;
            }

            ApiEnqueueRequest? enqueueRequest = JsonConvert.DeserializeObject<ApiEnqueueRequest>(jsonRequestString);
            if (enqueueRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Invalid enqueue request arguments.")));
                return;
            }

            IFormFileCollection uploadedFiles = httpContext.Request.Form.Files;

            if (uploadedFiles.Count > 1)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, "Only 1 file may be uploaded per request.")));
                return;
            }

            IFormFile uploadedFile = uploadedFiles[0];


            // Retrieve the subscription queue limit
            String retrieveSubscriptionQuery = @"
                        SELECT 
                            s.queue_limit
                        FROM 
                            subscriptions s
                        WHERE 
                            s.subscription_id = @SubscriptionId";

            MySqlCommand retrieveSubscriptionCommand = new MySqlCommand(retrieveSubscriptionQuery, this._databaseMySqlConnection);
            retrieveSubscriptionCommand.Parameters.AddWithValue("@SubscriptionId", user.SubscriptionId);

            Byte queueLimit = 0;
            MySqlDataReader reader = retrieveSubscriptionCommand.ExecuteReader();
            if (reader.Read())
            {
                queueLimit = reader.GetByte("queue_limit");
            }
            reader.Close();

            // Check the current number of queued tasks for the user
            String countQueuedTasksQuery = @"
                        SELECT 
                            COUNT(*) 
                        FROM 
                            tasks t
                        JOIN 
                            queue q ON t.task_id = q.task_id
                        WHERE 
                            t.user_id = @UserId";

            MySqlCommand countQueuedTasksCommand = new MySqlCommand(countQueuedTasksQuery, this._databaseMySqlConnection);
            countQueuedTasksCommand.Parameters.AddWithValue("@UserId", user.UserId);

            UInt64 currentQueuedTasks = Convert.ToUInt64(countQueuedTasksCommand.ExecuteScalar());

            // Compare against the queue limit
            if (currentQueuedTasks >= queueLimit)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Queue limit {queueLimit} reached for your subscription.")));
                return;
            }

            String retrieveEngineQuery = @"
                        SELECT 
                            e.engine_id,
                            e.name,
                            e.extension,
                            e.download_path,
                            e.render_argument
                        FROM 
                            engines e
                        WHERE 
                            e.engine_id = @EngineId";

            MySqlCommand retrieveEngineCommand = new MySqlCommand(retrieveEngineQuery, this._databaseMySqlConnection);
            retrieveEngineCommand.Parameters.AddWithValue("@EngineId", enqueueRequest.EngineId);

            reader = retrieveEngineCommand.ExecuteReader();

            ApiEngine? engine = null;

            if (reader.Read())
            {
                Byte engineId = reader.GetByte("engine_id");
                String name = reader.GetString("name");
                String extension = reader.GetString("extension");
                String downloadPath = reader.GetString("download_path");
                String renderArgument = reader.GetString("render_argument");

                engine = new(engineId, name, extension, downloadPath, renderArgument);
            }

            reader.Close();

            if (engine == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Engine with identifier {enqueueRequest.EngineId} not found.")));
                return;
            }

            if (engine.Extension != Path.GetExtension(uploadedFile.FileName))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Engine extension {engine.Extension} not matching with files extension {Path.GetExtension(uploadedFile.FileName)}.")));
                return;
            }
            DateTime queueTime = DateTime.Now;
            String directory = Path.Combine(engine.DownloadPath, user.UserId.ToString(), queueTime.Ticks.ToString());
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            String filePath = Path.Combine(directory, uploadedFile.FileName);
            await uploadedFile.CopyToAsync(new FileStream(filePath, FileMode.OpenOrCreate));

            UInt64 fileSize = (UInt64)uploadedFile.Length;
            String fileArguments = engine.RenderArgument;

            foreach (ApiArgType argumentType in enqueueRequest.Arguments)
            {
                String retrieveArgType = @"
                        SELECT 
                            a.argtype_id,
                            a.type,
                            a.regex
                        FROM 
                            argtypes a
                        WHERE 
                            a.argtype_id = @ArgTypeId";

                MySqlCommand argTypeCommand = new MySqlCommand(retrieveArgType, this._databaseMySqlConnection);
                argTypeCommand.Parameters.AddWithValue("@ArgTypeId", argumentType.ArgTypeId);

                reader = argTypeCommand.ExecuteReader();

                DbArgType? argType = null;

                if (reader.Read())
                {
                    String argTypeId = reader.GetString("argtype_id");
                    String type = reader.GetString("type");
                    String? regex = reader.GetString("regex");

                    argType = new DbArgType(argTypeId, type, regex);
                }

                reader.Close();

                if (argType == null)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Argument type identifier {argumentType.ArgTypeId} must be present in database.")));
                    return;
                }

                Boolean isValueAllowed = false;

                if (String.IsNullOrEmpty(argType.Regex))
                    switch (argType.Type)
                    {
                        default:
                            {
                                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Type {argType.Type} is not valid for RenderOnline.")));
                                return;
                            }
                        case "file":
                            // Allow only safe filenames (alphanumeric, underscore, hyphen, dot)
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^[a-zA-Z0-9_\-]+\.[a-zA-Z0-9]{1,4}$");
                            break;
                        case "path":
                            // Only allow absolute paths without any special shell characters
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^(/[a-zA-Z0-9_\-]+)+/?$");
                            break;
                        case "extension":
                            // File extensions: 1 to 4 alphabetic characters
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^\.[a-zA-Z0-9]{1,4}$");
                            break;
                        case "word":
                            // Single alphanumeric word
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^\w+$");
                            break;
                        case "sentence":
                            // Allow sentences but restrict special characters; avoid injection-prone ones
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^[a-zA-Z0-9\s,.!?'-]+$");
                            break;
                        case "natural":
                            // Positive integers only
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^\d+$");
                            break;
                        case "integers":
                            // Allow negative and positive integers
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^-?\d+$");
                            break;
                        case "real":
                            // Allow positive/negative floats and integers
                            isValueAllowed = Regex.IsMatch(argumentType.Value, @"^-?\d+(\.\d+)?$");
                            break;
                    }
                else if (argType.Regex != null) isValueAllowed = Regex.IsMatch(argumentType.Value, argumentType.Regex);

                if (!isValueAllowed)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(false, $"Value {argumentType.Value} must be of type {argType.Type}.")));
                    return;
                }

                fileArguments = fileArguments.Replace($"$RENDERONLINE:{argumentType.ArgTypeId}", argumentType.Value);
            }

            fileArguments = fileArguments.Replace("$RENDERONLINE:@uploaded_file", filePath);

            String addRenderQuery = @"
                        INSERT INTO renders (file_name, file_path, file_size, arguments, engine_id) 
                        VALUES (@FileName, @FilePath, @FileSize, @Arguments, @EngineId);
                        SELECT LAST_INSERT_ID();";

            MySqlCommand addRenderCommand = new MySqlCommand(addRenderQuery, this._databaseMySqlConnection);
            addRenderCommand.Parameters.AddWithValue("@FileName", uploadedFile.FileName);
            addRenderCommand.Parameters.AddWithValue("@FilePath", filePath);
            addRenderCommand.Parameters.AddWithValue("@FileSize", fileSize);
            addRenderCommand.Parameters.AddWithValue("@Arguments", fileArguments);
            addRenderCommand.Parameters.AddWithValue("@EngineId", engine.EngineId);

            UInt64 renderId = Convert.ToUInt64(addRenderCommand.ExecuteScalar());

            String addTaskQuery = @"
                        INSERT INTO tasks (user_id, queue_time, start_time, end_time, is_running, is_success, render_id, machine_id) 
                        VALUES (@UserId, @QueueTime, NULL, NULL, @IsRunning, @IsSuccess, @RenderId, NULL);
                        SELECT LAST_INSERT_ID();";

            MySqlCommand addTaskCommand = new MySqlCommand(addTaskQuery, this._databaseMySqlConnection);
            addTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);
            addTaskCommand.Parameters.AddWithValue("@QueueTime", queueTime);
            addTaskCommand.Parameters.AddWithValue("@IsRunning", false);
            addTaskCommand.Parameters.AddWithValue("@IsSuccess", false);
            addTaskCommand.Parameters.AddWithValue("@RenderId", renderId);

            UInt64 taskId = Convert.ToUInt64(addTaskCommand.ExecuteScalar());


            String addQueueQuery = @"
                        INSERT INTO queue (task_id) 
                        VALUES (@TaskId);";

            MySqlCommand addQueueCommand = new MySqlCommand(addQueueQuery, this._databaseMySqlConnection);
            addQueueCommand.Parameters.AddWithValue("@TaskId", taskId);

            // Execute queue insertion
            addQueueCommand.ExecuteNonQuery();

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiEnqueueResponse(true, "Task successfully enqueued.")));
        }

        public async Task HandleDequeueRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, "Request content type must be application/json.")));
                return;
            }

            ApiDequeueRequest? dequeueRequest = await httpContext.Request.ReadFromJsonAsync<ApiDequeueRequest>();

            if (dequeueRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, "Request content has an invalid format.")));
                return;
            }

            String checkTaskQuery = @"
                SELECT 
                    t.task_id 
                FROM 
                    tasks t 
                WHERE 
                    t.task_id = @TaskId AND t.user_id = @UserId";

            MySqlCommand checkTaskCommand = new MySqlCommand(checkTaskQuery, this._databaseMySqlConnection);
            checkTaskCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);
            checkTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);

            MySqlDataReader reader = checkTaskCommand.ExecuteReader();

            if (!reader.Read())
            {
                reader.Close();
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, $"Task with identifier {dequeueRequest.TaskId} not found for the current user.")));
                return;
            }

            reader.Close();

            // Check if the task is in the queue
            String checkQueueQuery = @"
                SELECT 
                    q.queue_id 
                FROM 
                    queue q 
                WHERE 
                    q.task_id = @TaskId";

            MySqlCommand checkQueueCommand = new MySqlCommand(checkQueueQuery, this._databaseMySqlConnection);
            checkQueueCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);

            reader = checkQueueCommand.ExecuteReader();

            if (!reader.Read())
            {
                reader.Close();
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, $"Task with identifier {dequeueRequest.TaskId} is not in the queue.")));
                return;
            }

            reader.Close();

            // Delete the task from the queue
            String deleteQueueQuery = @"
                DELETE FROM queue 
                WHERE task_id = @TaskId";

            MySqlCommand deleteQueueCommand = new MySqlCommand(deleteQueueQuery, this._databaseMySqlConnection);

            deleteQueueCommand.ExecuteNonQuery();

            // Updated query to include arguments from the renders table
            String retrieveFullTaskQuery = @"
                                SELECT 
                                    t.task_id, 
                                    t.user_id,
                                    t.queue_time,
                                    t.start_time, 
                                    t.end_time, 
                                    t.is_running, 
                                    t.is_success,
                                    t.render_id,
                                    t.machine_id,
                                    r.render_id,
                                    r.file_name,
                                    r.file_path,
                                    r.file_size,
                                    r.arguments,
                                    r.engine_id,
                                    e.engine_id,
                                    e.name,
                                    e.extension,
                                    e.download_path,
                                    e.render_argument
                                FROM 
                                    tasks t
                                JOIN 
                                    queue q ON t.task_id = q.task_id
                                JOIN 
                                    renders r ON t.render_id = r.render_id
                                JOIN
                                    engines e ON r.engine_id = e.engine_id
                                WHERE 
                                    t.task_id = @TaskId";

            ApiTaskInfo? task = null;

            MySqlCommand retrieveFullTaskCommand = new MySqlCommand(retrieveFullTaskQuery, this._databaseMySqlConnection);
            retrieveFullTaskCommand.Parameters.AddWithValue("@TaskId", dequeueRequest.TaskId);

            reader = retrieveFullTaskCommand.ExecuteReader();

            if (reader.Read())
            {
                // Read task details
                UInt64 taskId = reader.GetUInt64("task_id");
                UInt16 userId = reader.GetUInt16("user_id");
                DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                Boolean isRunning = reader.GetBoolean("is_running");
                Boolean isSuccess = reader.GetBoolean("is_success");
                UInt64 renderId = reader.GetUInt64("render_id");
                Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                // Read render details
                String fileName = reader.GetString("file_name");
                String filePath = reader.GetString("file_path");
                UInt64 fileSize = reader.GetUInt64("file_size");
                String arguments = reader.GetString("arguments");
                Byte engineId = reader.GetByte("engine_id");

                DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                // Read engine details
                String engineName = reader.GetString("name");
                String extension = reader.GetString("extension");
                String downloadPath = reader.GetString("download_path");
                String renderArgument = reader.GetString("render_argument");

                DbEngine dbEngine = new(engineId, engineName, extension, downloadPath, renderArgument);

                ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);
                ApiEngine apiEngine = new(dbEngine.EngineId, dbEngine.Name, dbEngine.Extension, dbEngine.DownloadPath, dbEngine.RenderArgument);

                task = new ApiTaskInfo(apiTask, apiRender, apiEngine);
            }
            reader.Close();

            if (task != null && task?.Task?.MachineId != null)
            {
                String machineQuery = @"
                        SELECT 
                            m.machine_id, 
                            m.ip_address,
                            m.port 
                        FROM 
                            machines m
                        WHERE m.machine_id = @MachineId";


                MySqlCommand machineCommand = new MySqlCommand(machineQuery, this._databaseMySqlConnection);
                machineCommand.Parameters.AddWithValue("@MachineId", task.Task.MachineId);
                MySqlDataReader machineReader = machineCommand.ExecuteReader();

                DbMachine? machine = null;

                if (machineReader.Read())
                {
                    Byte machineId = machineReader.GetByte("machine_id");
                    String ipAddress = machineReader.GetString("ip_address");
                    UInt16 port = machineReader.GetUInt16("port");

                    machine = new DbMachine(machineId, ipAddress, port);
                }
                machineReader.Close();

                if (machine != null)
                    await StopTaskOnMachine(machine, task);
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(true, "Task successfully dequeued.")));
        }

        public async Task HandleDownloadRequest(HttpContext httpContext)
        {
            ApiUser? user = AuthenticateAndGetUserInDatabase(httpContext);

            if (user == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsync(String.Empty);
                return;
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, "Request content type must be application/json.")));
                return;
            }

            ApiDownloadRequest? downloadRequest = await httpContext.Request.ReadFromJsonAsync<ApiDownloadRequest>();

            if (downloadRequest == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, "Request content has an invalid format.")));
                return;
            }

            String checkTaskQuery = @"
                SELECT 
                    t.task_id 
                FROM 
                    tasks t 
                WHERE 
                    t.task_id = @TaskId AND t.user_id = @UserId";

            MySqlCommand checkTaskCommand = new MySqlCommand(checkTaskQuery, this._databaseMySqlConnection);
            checkTaskCommand.Parameters.AddWithValue("@TaskId", downloadRequest.TaskId);
            checkTaskCommand.Parameters.AddWithValue("@UserId", user.UserId);

            MySqlDataReader reader = checkTaskCommand.ExecuteReader();

            if (!reader.Read())
            {
                reader.Close();
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(new ApiDequeueResponse(false, $"Task with identifier {downloadRequest.TaskId} not found for the current user.")));
                return;
            }
            reader.Close();


        }

        public void StartPollingService()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(15000);
                    Console.WriteLine("Polling...");

                    try
                    {
                        // Updated query to include arguments from the renders table
                        String query = @"
                                SELECT 
                                    t.task_id, 
                                    t.user_id,
                                    t.queue_time,
                                    t.start_time, 
                                    t.end_time, 
                                    t.is_running, 
                                    t.is_success,
                                    t.render_id,
                                    t.machine_id,
                                    r.render_id,
                                    r.file_name,
                                    r.file_path,
                                    r.file_size,
                                    r.arguments,
                                    r.engine_id,
                                    e.engine_id,
                                    e.name,
                                    e.extension,
                                    e.download_path,
                                    e.render_argument
                                FROM 
                                    tasks t
                                JOIN 
                                    queue q ON t.task_id = q.task_id
                                JOIN 
                                    renders r ON t.render_id = r.render_id
                                JOIN
                                    engines e ON r.engine_id = e.engine_id
                                WHERE 
                                    t.is_success = 0";

                        List<ApiTaskInfo> tasks = new();

                        MySqlCommand command = new MySqlCommand(query, this._databaseMySqlConnection);
                        MySqlDataReader reader = command.ExecuteReader();

                        while (reader.Read())
                        {
                            // Read task details
                            UInt64 taskId = reader.GetUInt64("task_id");
                            UInt16 userId = reader.GetUInt16("user_id");
                            DateTime? queueTime = !reader.IsDBNull("queue_time") ? reader.GetDateTime("queue_time") : null;
                            DateTime? startTime = !reader.IsDBNull("start_time") ? reader.GetDateTime("start_time") : null;
                            DateTime? endTime = !reader.IsDBNull("end_time") ? reader.GetDateTime("end_time") : null;
                            Boolean isRunning = reader.GetBoolean("is_running");
                            Boolean isSuccess = reader.GetBoolean("is_success");
                            UInt64 renderId = reader.GetUInt64("render_id");
                            Byte? machineId = !reader.IsDBNull("machine_id") ? reader.GetByte("machine_id") : null;

                            DbTask dbTask = new(taskId, userId, queueTime, startTime, endTime, isRunning, isSuccess, renderId, machineId);

                            // Read render details
                            String fileName = reader.GetString("file_name");
                            String filePath = reader.GetString("file_path");
                            UInt64 fileSize = reader.GetUInt64("file_size");
                            String arguments = reader.GetString("arguments");
                            Byte engineId = reader.GetByte("engine_id");

                            DbRender dbRender = new(renderId, fileName, filePath, fileSize, arguments, engineId);

                            // Read engine details
                            String engineName = reader.GetString("name");
                            String extension = reader.GetString("extension");
                            String downloadPath = reader.GetString("download_path");
                            String renderArgument = reader.GetString("render_argument");

                            DbEngine dbEngine = new(engineId, engineName, extension, downloadPath, renderArgument);

                            ApiTask apiTask = new(dbTask.TaskId, dbTask.UserId, dbTask.QueueTime, dbTask.StartTime, dbTask.EndTime, dbTask.IsRunning, dbTask.IsSuccess, dbTask.RenderId, dbTask.MachineId);
                            ApiRender apiRender = new(dbRender.RenderId, dbRender.FileName, dbRender.FileSize, dbRender.Arguments, dbRender.EngineId);
                            ApiEngine apiEngine = new(dbEngine.EngineId, dbEngine.Name, dbEngine.Extension, dbEngine.DownloadPath, dbEngine.RenderArgument);

                            tasks.Add(new ApiTaskInfo(apiTask, apiRender, apiEngine));
                        }
                        reader.Close();

                        foreach (ApiTaskInfo task in tasks)
                        {
                            if (task?.Task?.MachineId == null && task?.Task?.IsRunning == false)
                            {
                                Console.WriteLine($"Attempting to assign task {task.Task.TaskId} on machine: {task.Task.MachineId}");

                                // Assign tasks to machines if not already assigned
                                await AssignTaskToMachine(task);
                            }
                            else
                            {
                                Console.WriteLine($"Checking status of previously started task {task?.Task?.TaskId}");

                                // Check the status of tasks already assigned to machines
                                if (task?.Task != null)
                                    await CheckTaskStatusOnMachine(task.Task);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log any errors during polling
                        Console.WriteLine($"Polling service error: {ex.Message}");
                        Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                    }
                }
            });
        }

        private async Task AssignTaskToMachine(ApiTaskInfo task)
        {
            try
            {
                String machineQuery = @"
                        SELECT 
                            m.machine_id, 
                            m.ip_address,
                            m.port 
                        FROM 
                            machines m";

                MySqlCommand machineCommand = new MySqlCommand(machineQuery, this._databaseMySqlConnection);
                MySqlDataReader machineReader = machineCommand.ExecuteReader();

                List<DbMachine> machines = new();

                while (machineReader.Read())
                {
                    Byte machineId = machineReader.GetByte("machine_id");
                    String ipAddress = machineReader.GetString("ip_address");
                    UInt16 port = machineReader.GetUInt16("port");

                    machines.Add(new DbMachine(machineId, ipAddress, port));
                }
                machineReader.Close();

                foreach (DbMachine machine in machines)
                {
                    HttpClient httpClient = new HttpClient(this._httpClientHandler);
                    httpClient.BaseAddress = new Uri($"https://{machine.IpAddress}:{machine.Port}");
                    HttpResponseMessage statusResponse = await httpClient.GetAsync("/hpc/status");

                    if (statusResponse.IsSuccessStatusCode)
                    {
                        String statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                        HPCStatusResponse? status = JsonConvert.DeserializeObject<HPCStatusResponse>(statusResponseContent);

                        if (status == null)
                        {
                            Console.WriteLine($"Machine {machine.MachineId} invalid response!");
                            continue;
                        }

                        if (status.Task == null || !status.Task.IsRunning)
                        {
                            // Attempt to start task on available machine
                            if (await StartTaskOnMachine(machine, task))
                            {
                                UpdateTaskStartDetails(task.Task.TaskId, machine.MachineId);
                                break;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Failed to get status from machine {machine.MachineId}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in assigning task to machine: {ex.Message}");
            }
        }

        private async Task<Boolean> StartTaskOnMachine(DbMachine machine, ApiTaskInfo task)
        {
            try
            {
                Console.WriteLine($"Attempting to start task {task.Task.TaskId} on machine {machine.MachineId}...");

                HPCStartArgs startArgs = new(task.Engine.Name, task.Task.TaskId, task.Render.Arguments);
                String startMessage = JsonConvert.SerializeObject(startArgs);

                HttpClient httpClient = new HttpClient(this._httpClientHandler);
                httpClient.BaseAddress = new Uri($"https://{machine.IpAddress}:{machine.Port}");
                HttpContent content = new StringContent(startMessage, Encoding.UTF8, "application/json");
                HttpResponseMessage startResponse = await httpClient.PostAsync("/hpc/start", content);

                if (startResponse.IsSuccessStatusCode)
                {
                    String startResponseContent = await startResponse.Content.ReadAsStringAsync();
                    HPCStartResponse? start = JsonConvert.DeserializeObject<HPCStartResponse>(startResponseContent);

                    if (start != null && start.IsSuccess)
                    {
                        Console.WriteLine($"Succeeded to start task {task.Task.TaskId} on machine {machine.MachineId}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to start task {task.Task.TaskId} on machine {machine.MachineId}");
                    }
                }
                else
                {
                    Console.WriteLine($"HTTP error while starting task {task.Task.TaskId} on machine {machine.MachineId}: {startResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while starting task {task?.Task?.TaskId} on machine {machine.MachineId}: {ex.Message}");
            }

            return false;
        }

        private async Task<Boolean> StopTaskOnMachine(DbMachine machine, ApiTaskInfo task)
        {
            try
            {
                Console.WriteLine($"Attempting to stop task {task.Task.TaskId} on machine {machine.MachineId}...");

                HPCStopArgs stopArgs = new(task.Task.TaskId);
                String stopMessage = JsonConvert.SerializeObject(stopArgs);

                HttpClient httpClient = new HttpClient(this._httpClientHandler);
                httpClient.BaseAddress = new Uri($"https://{machine.IpAddress}:{machine.Port}");
                HttpContent content = new StringContent(stopMessage, Encoding.UTF8, "application/json");
                HttpResponseMessage stopResponse = await httpClient.PostAsync("/hpc/stop", content);

                if (stopResponse.IsSuccessStatusCode)
                {
                    String startResponseContent = await stopResponse.Content.ReadAsStringAsync();
                    HPCStartResponse? stop = JsonConvert.DeserializeObject<HPCStartResponse>(startResponseContent);

                    if (stop != null && stop.IsSuccess)
                    {
                        Console.WriteLine($"Succeeded to stop task {task.Task.TaskId} on machine {machine.MachineId}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to stop task {task.Task.TaskId} on machine {machine.MachineId}");
                    }
                }
                else
                {
                    Console.WriteLine($"HTTP error while stopping task {task.Task.TaskId} on machine {machine.MachineId}: {stopResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while stopping task {task?.Task?.TaskId} on machine {machine.MachineId}: {ex.Message}");
            }

            return false;
        }

        private void UpdateTaskStartDetails(UInt64 taskId, Byte machineId)
        {
            try
            {
                String updateTaskQuery = @"
                        UPDATE tasks 
                        SET start_time = @StartTime, is_running = 1, machine_id = @MachineId 
                        WHERE task_id = @TaskId";

                using (MySqlCommand updateTaskCommand = new MySqlCommand(updateTaskQuery, this._databaseMySqlConnection))
                {
                    updateTaskCommand.Parameters.AddWithValue("@StartTime", DateTime.Now);
                    updateTaskCommand.Parameters.AddWithValue("@MachineId", machineId);
                    updateTaskCommand.Parameters.AddWithValue("@TaskId", taskId);
                    updateTaskCommand.ExecuteNonQuery();
                }
                Console.WriteLine($"Task {taskId} successfully started on machine {machineId}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update task start details for Task {taskId}: {ex.Message}");
            }
        }

        private async Task CheckTaskStatusOnMachine(ApiTask task)
        {
            if (task.MachineId == null)
            {
                Console.WriteLine("Task has no assigned machine.");
                return;
            }

            try
            {
                String machineQuery = @"
                        SELECT 
                            m.machine_id, 
                            m.ip_address, 
                            m.port 
                        FROM 
                            machines m
                        WHERE 
                            m.machine_id = @MachineId";

                using (MySqlCommand machineCommand = new MySqlCommand(machineQuery, this._databaseMySqlConnection))
                {
                    machineCommand.Parameters.AddWithValue("@MachineId", task.MachineId);
                    using (MySqlDataReader reader = machineCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            String ipAddress = reader.GetString("ip_address");
                            UInt16 port = reader.GetUInt16("port");
                            reader.Close();

                            HttpClient httpClient = new HttpClient(this._httpClientHandler);
                            httpClient.BaseAddress = new Uri($"https://{ipAddress}:{port}");
                            HttpResponseMessage statusResponse = await httpClient.GetAsync("/hpc/status");

                            if (statusResponse.IsSuccessStatusCode)
                            {
                                String statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                                HPCStatusResponse? status = JsonConvert.DeserializeObject<HPCStatusResponse>(statusResponseContent);

                                if (status == null)
                                {
                                    Console.WriteLine($"Machine {task.MachineId} invalid response!");
                                    return;
                                }

                                if (status.Task != null)
                                {
                                    if (!status.Task.IsRunning && status.Task.IsSuccess)
                                    {
                                        CompleteTask(task.TaskId);
                                    }
                                    else if (!status.Task.IsRunning && !status.Task.IsSuccess)
                                    {
                                        HandleTaskFailure(task.TaskId, (Byte)task.MachineId);
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Failed to get status from machine {task.MachineId}: {statusResponse.StatusCode}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Machine with ID {task.MachineId} not found.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking task status on machine {task.MachineId}: {ex.Message}");
            }
        }

        private void CompleteTask(UInt64 taskId)
        {
            try
            {
                String completeTaskQuery = @"
                        UPDATE tasks 
                        SET end_time = @EndTime, is_running = 0, is_success = 1 
                        WHERE task_id = @TaskId";

                using (MySqlCommand completeTaskCommand = new MySqlCommand(completeTaskQuery, this._databaseMySqlConnection))
                {
                    completeTaskCommand.Parameters.AddWithValue("@EndTime", DateTime.Now);
                    completeTaskCommand.Parameters.AddWithValue("@TaskId", taskId);
                    completeTaskCommand.ExecuteNonQuery();
                }

                RemoveTaskFromQueue(taskId);
                Console.WriteLine($"Task {taskId} completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error completing task {taskId}: {ex.Message}");
            }
        }

        private void HandleTaskFailure(UInt64 taskId, Byte machineId)
        {
            try
            {
                String failTaskQuery = @"
                            UPDATE tasks 
                            SET start_time = @StartTime, end_time = @EndTime, is_running = 0, is_success = 0, machine_id = @MachineId 
                            WHERE task_id = @TaskId";

                using (MySqlCommand failTaskCommand = new MySqlCommand(failTaskQuery, this._databaseMySqlConnection))
                {
                    failTaskCommand.Parameters.AddWithValue("@StartTime", DateTime.Now);
                    failTaskCommand.Parameters.AddWithValue("@EndTime", DateTime.Now);
                    failTaskCommand.Parameters.AddWithValue("@MachineId", machineId);
                    failTaskCommand.Parameters.AddWithValue("@TaskId", taskId);
                    failTaskCommand.ExecuteNonQuery();
                }

                RemoveTaskFromQueue(taskId);
                Console.WriteLine($"Task {taskId} failed on machine {machineId}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling task failure for Task {taskId} on Machine {machineId}: {ex.Message}");
            }
        }

        private void RemoveTaskFromQueue(UInt64 taskId)
        {
            try
            {
                String deleteQueueQuery = @"
                        DELETE FROM queue 
                        WHERE task_id = @TaskId";

                using (MySqlCommand deleteQueueCommand = new MySqlCommand(deleteQueueQuery, this._databaseMySqlConnection))
                {
                    deleteQueueCommand.Parameters.AddWithValue("@TaskId", taskId);
                    deleteQueueCommand.ExecuteNonQuery();
                }
                Console.WriteLine($"Task {taskId} removed from queue.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing task {taskId} from queue: {ex.Message}");
            }
        }
    }
}

