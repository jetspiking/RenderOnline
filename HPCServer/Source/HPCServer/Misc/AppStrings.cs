namespace HPCServer.Misc
{
    public class AppStrings
    {
        public static readonly String AppTitle = "HPCServer";

        public static readonly String SuccessStoppingRender = "Active render terminated!";
        public static readonly String SuccessNoActiveRender = "No active render!";
        public static readonly String SuccessUsingRenderingEngine = "Rendering using engine with identifier: ";

        public static readonly String ErrorStoppingRender = "Failed to terminate the current render task!";
        public static readonly String ErrorInvalidTaskId = "No process running with provided task identifier!";
        public static readonly String ErrorInvalidRenderArgs = "Invalid render arguments!";
        public static readonly String ErrorRenderInProgress = "There already is an active render!";
        public static readonly String ErrorInvalidRenderEngineArgument = "Invalid render engine argument!";
        public static readonly String ErrorConfigurationNotFoundOrInvalid = "Configuration not found or invalid!";
        public static readonly String ErrorRequestedRenderingEngineNotConfigured = "Requested rendering engine not configured: ";
    }
}
