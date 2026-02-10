namespace AutomationAPI.Services;

public interface IAutomationService
{
    Task ExecuteWebAutomation(Models.Registro registro);
}