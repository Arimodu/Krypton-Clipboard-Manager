using System;
using System.Reactive;
using System.Threading.Tasks;
using Krypton_Desktop.Services;
using ReactiveUI;
using Serilog;

namespace Krypton_Desktop.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly ServerConnectionService _connectionService;
    private readonly SettingsService _settingsService;
    private readonly Action<bool> _closeAction;

    private string _serverAddress = string.Empty;
    private int _serverPort = 6789;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _apiKey = string.Empty;
    private string _connectButtonText = "Connect";
    private bool _useApiKey;
    private bool _isConnecting;
    private bool _isConnected;
    private bool _isAuthenticating;
    private string? _errorMessage;
    private string? _successMessage;
    private int _currentStep = 1;

    public string ServerAddress
    {
        get => _serverAddress;
        set => this.RaiseAndSetIfChanged(ref _serverAddress, value);
    }

    public int ServerPort
    {
        get => _serverPort;
        set => this.RaiseAndSetIfChanged(ref _serverPort, value);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }
    public string ConnectButtonText
    {
        get => _connectButtonText;
        set => this.RaiseAndSetIfChanged(ref _connectButtonText, value);
    }

    public bool UseApiKey
    {
        get => _useApiKey;
        set => this.RaiseAndSetIfChanged(ref _useApiKey, value);
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set => this.RaiseAndSetIfChanged(ref _isConnecting, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        set => this.RaiseAndSetIfChanged(ref _isAuthenticating, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string? SuccessMessage
    {
        get => _successMessage;
        set => this.RaiseAndSetIfChanged(ref _successMessage, value);
    }

    public int CurrentStep
    {
        get => _currentStep;
        set => this.RaiseAndSetIfChanged(ref _currentStep, value);
    }

    public bool CanConnect => !IsConnecting && !IsConnected && !string.IsNullOrWhiteSpace(ServerAddress);
    public bool CanAuthenticate => IsConnected && !IsAuthenticating;
    public bool ShowUsernamePassword => !UseApiKey;
    public bool ShowApiKey => UseApiKey;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> LoginCommand { get; }
    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }

    public LoginViewModel(
        ServerConnectionService connectionService,
        SettingsService settingsService,
        Action<bool> closeAction)
    {
        _connectionService = connectionService;
        _settingsService = settingsService;
        _closeAction = closeAction;

        // Load saved settings
        var settings = _settingsService.Settings;
        _serverAddress = settings.ServerAddress ?? string.Empty;
        _serverPort = settings.ServerPort;
        _apiKey = settings.ApiKey ?? string.Empty;
        _useApiKey = !string.IsNullOrEmpty(_apiKey);

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync);
        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
        RegisterCommand = ReactiveCommand.CreateFromTask(RegisterAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        BackCommand = ReactiveCommand.CreateFromTask(GoBackAsync);

        // Auto-update computed properties
        this.WhenAnyValue(x => x.IsConnecting, x => x.IsConnected, x => x.ServerAddress)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CanConnect)));

        this.WhenAnyValue(x => x.IsConnected, x => x.IsAuthenticating)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CanAuthenticate)));

        this.WhenAnyValue(x => x.UseApiKey)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(ShowUsernamePassword));
                this.RaisePropertyChanged(nameof(ShowApiKey));
            });

        this.WhenAnyValue(x => x.CurrentStep)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsStep1));
                this.RaisePropertyChanged(nameof(IsStep2));
            });
    }

    private async Task ConnectAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        IsConnecting = true;
        ConnectButtonText = "Connecting...";

        try
        {
            var success = await _connectionService.ConnectAsync(ServerAddress, ServerPort);
            if (success)
            {
                IsConnected = true;
                ConnectButtonText = "Connected";
                SuccessMessage = "Connected to server";

                // Move to authentication step
                CurrentStep = 2;

                // If we have an API key, try to authenticate automatically
                if (UseApiKey && !string.IsNullOrWhiteSpace(ApiKey))
                {
                    await AuthenticateWithApiKeyAsync();
                }
            }
            else
            {
                ConnectButtonText = "Connect";
                ErrorMessage = _connectionService.LastError ?? "Failed to connect";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Connection failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task LoginAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        if (UseApiKey)
        {
            await AuthenticateWithApiKeyAsync();
        }
        else
        {
            await AuthenticateWithPasswordAsync();
        }
    }

    private async Task AuthenticateWithApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ErrorMessage = "API key is required";
            return;
        }

        IsAuthenticating = true;

        try
        {
            var success = await _connectionService.AuthenticateWithApiKeyAsync(ApiKey);
            if (success)
            {
                SaveSettings();
                _closeAction(true);
            }
            else
            {
                ErrorMessage = _connectionService.LastError ?? "Authentication failed";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "API key authentication failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private async Task AuthenticateWithPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required";
            return;
        }

        IsAuthenticating = true;

        try
        {
            var (success, apiKey, error) = await _connectionService.AuthenticateWithPasswordAsync(Username, Password);
            if (success)
            {
                // Save the API key for future use
                if (!string.IsNullOrEmpty(apiKey))
                {
                    ApiKey = apiKey;
                }
                SaveSettings();
                _closeAction(true);
            }
            else
            {
                ErrorMessage = error ?? "Authentication failed";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Password authentication failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private async Task RegisterAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required";
            return;
        }

        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters";
            return;
        }

        IsAuthenticating = true;

        try
        {
            var (success, apiKey, error) = await _connectionService.RegisterAsync(Username, Password);
            if (success)
            {
                if (!string.IsNullOrEmpty(apiKey))
                {
                    ApiKey = apiKey;
                }
                SaveSettings();
                _closeAction(true);
            }
            else
            {
                ErrorMessage = error ?? "Registration failed";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Registration failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private void SaveSettings()
    {
        var settings = _settingsService.Settings;
        settings.ServerAddress = ServerAddress;
        settings.ServerPort = ServerPort;
        settings.ApiKey = ApiKey;
        _settingsService.Save();
    }

    private async Task GoBackAsync()
    {
        // Disconnect from server
        await _connectionService.DisconnectAsync();

        // Reset state
        IsConnected = false;
        ConnectButtonText = "Connect";
        ErrorMessage = null;
        SuccessMessage = null;

        // Go back to step 1
        CurrentStep = 1;
    }

    private void Cancel()
    {
        _closeAction(false);
    }
}
