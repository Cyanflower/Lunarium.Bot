
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.DataCenter;

public class InitializationService
{
    private readonly ILogger _logger;
    public bool NeedInitialization { get; private set; } = false;
    public InitializationService(ILogger logger)
    {
        _logger = logger.ForContext("Initialization");
        NeedInitialization = CheckingInitialization();
    }

    public void Failed()
    {
        NeedInitialization = false;
    }

    public void Complete()
    {
        NeedInitialization = false;
    }
    
    private bool CheckingInitialization()
    {
        try
        {
            string flagPath = Path.Combine(AppContext.BaseDirectory, "Initialized.flag");
            if (File.Exists(flagPath))
            {
                return false;
            }
            else
            {
                File.WriteAllText(flagPath, string.Empty);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            return false;
        }
    }
}