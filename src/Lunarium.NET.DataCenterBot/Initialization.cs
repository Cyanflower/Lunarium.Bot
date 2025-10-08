
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.DataCenter;

public class Initialization
{
    private readonly ILogger _logger;
    public bool NeedInitialization { get; private set; } = false;
    public Initialization(ILogger logger)
    {
        _logger = logger.ForContext("Initialization");
        NeedInitialization = CheckingInitialization();
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