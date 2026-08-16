using CommunityToolkit.Mvvm.ComponentModel;
using SQLConstruct.Services;

namespace SQLConstruct.ViewModels;

public partial class ConnectionDialogViewModel : ObservableObject
{
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private string _sqlitePath = "";
    [ObservableProperty] private string _host = "localhost";
    [ObservableProperty] private string _port = "5432";
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private string _username = "postgres";
    [ObservableProperty] private string _error = "";

    // MS SQL Server
    [ObservableProperty] private string _sqlServer = "localhost";
    [ObservableProperty] private string _sqlDatabase = "";
    [ObservableProperty] private string _sqlUsername = "sa";
    [ObservableProperty] private bool _sqlUseWindowsAuth;

    [ObservableProperty] private string _password = "";

    public bool HasError => Error.Length > 0;

    public ConnectionSettings? Result { get; private set; }

    partial void OnErrorChanged(string value)
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasError)));
    }

    public bool Apply()
    {
        switch (ModeIndex)
        {
            case 0: // SQLite
            {
                if (string.IsNullOrWhiteSpace(SqlitePath) || !File.Exists(SqlitePath))
                {
                    Error = "Укажите существующий файл базы SQLite.";
                    return false;
                }
                Result = new ConnectionSettings { Kind = ConnectionKind.Sqlite, SqlitePath = SqlitePath };
                return true;
            }
            case 1: // PostgreSQL
            {
                if (string.IsNullOrWhiteSpace(Host))
                {
                    Error = "Укажите сервер.";
                    return false;
                }
                if (!int.TryParse(Port.Trim(), out _))
                {
                    Error = "Порт должен быть числом.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(Database))
                {
                    Error = "Укажите имя базы данных.";
                    return false;
                }
                Result = new ConnectionSettings
                {
                    Kind = ConnectionKind.Postgres,
                    Host = Host.Trim(),
                    Port = Port.Trim(),
                    Database = Database.Trim(),
                    Username = Username.Trim(),
                    Password = Password
                };
                return true;
            }
            default: // MS SQL Server
            {
                if (string.IsNullOrWhiteSpace(SqlServer))
                {
                    Error = "Укажите сервер (например localhost, localhost,1433 или localhost\\SQLEXPRESS).";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(SqlDatabase))
                {
                    Error = "Укажите имя базы данных.";
                    return false;
                }
                if (!SqlUseWindowsAuth && string.IsNullOrWhiteSpace(SqlUsername))
                {
                    Error = "Укажите имя пользователя или включите Windows-аутентификацию.";
                    return false;
                }
                Result = new ConnectionSettings
                {
                    Kind = ConnectionKind.SqlServer,
                    Host = SqlServer.Trim(),
                    Database = SqlDatabase.Trim(),
                    Username = SqlUsername.Trim(),
                    Password = Password,
                    UseWindowsAuth = SqlUseWindowsAuth
                };
                return true;
            }
        }
    }
}
