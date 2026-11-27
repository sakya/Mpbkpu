using System;
using System.Collections.Generic;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Controls.Models;

public sealed class PositionDetailModel : BaseModel
{
    private Job.Position.FileSystemTypes _type;
    public Job.Position.FileSystemTypes Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value)) {
                OnPropertyChanged(nameof(IsLocalFileSystem));
                OnPropertyChanged(nameof(PathHint));
                OnPropertyChanged(nameof(SupportUser));
                OnPropertyChanged(nameof(SupportPassword));
                OnPropertyChanged(nameof(SupportOAuth));
                OnPropertyChanged(nameof(SupportOAuthServerUrl));
            }
        }
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set => SetField(ref _isTesting, value);
    }

    private bool? _testResult;
    /// <summary>
    /// Result of the last connection test, null when not tested (or the settings changed since)
    /// </summary>
    public bool? TestResult
    {
        get => _testResult;
        set
        {
            if (SetField(ref _testResult, value)) {
                OnPropertyChanged(nameof(TestSucceeded));
                OnPropertyChanged(nameof(TestFailed));
            }
        }
    }

    public bool TestSucceeded => TestResult == true;
    public bool TestFailed => TestResult == false;

    private string? _testMessage;
    public string? TestMessage
    {
        get => _testMessage;
        set => SetField(ref _testMessage, value);
    }

    private List<ErrorContainer.ErrorData>? _errors;
    public List<ErrorContainer.ErrorData>? Errors
    {
        get => _errors;
        set => SetField(ref _errors, value);
    }

    private string? _path;
    public string? Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    private string? _user;
    public string? User
    {
        get => _user;
        set => SetField(ref _user, value);
    }

    private string? _password;
    public string? Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    private Uri? _oauthServerUrl;
    public Uri? OauthServerUrl
    {
        get => _oauthServerUrl;
        set => SetField(ref _oauthServerUrl, value);
    }

    private Uri? _oauthRedirectUrl;
    public Uri? OauthRedirectUrl
    {
        get => _oauthRedirectUrl;
        set => SetField(ref _oauthRedirectUrl, value);
    }

    private string? _oauthAppName;
    public string? OauthAppName
    {
        get => _oauthAppName;
        set => SetField(ref _oauthAppName, value);
    }

    private string? _oauthAppId;
    public string? OauthAppId
    {
        get => _oauthAppId;
        set => SetField(ref _oauthAppId, value);
    }

    private string? _oauthAppSecret;
    public string? OauthAppSecret
    {
        get => _oauthAppSecret;
        set => SetField(ref _oauthAppSecret, value);
    }

    public bool IsLocalFileSystem => Type == Job.Position.FileSystemTypes.FileSystem;

    /// <summary>
    /// The options a path of this type accepts (FileManagerFactory), null when there are none
    /// </summary>
    public string? PathHint => Type switch
    {
        Job.Position.FileSystemTypes.Ftp => Localizer.Localizer.Instance["PathHintFtp"],
        Job.Position.FileSystemTypes.Sftp => Localizer.Localizer.Instance["PathHintSftp"],
        _ => null
    };

    public bool SupportUser =>
        Type is Job.Position.FileSystemTypes.Ftp or Job.Position.FileSystemTypes.Mega or Job.Position.FileSystemTypes.Nextcloud or
            Job.Position.FileSystemTypes.Sftp or Job.Position.FileSystemTypes.Smb or Job.Position.FileSystemTypes.MediaFire
            or Job.Position.FileSystemTypes.OneDrive or Job.Position.FileSystemTypes.DropBox or Job.Position.FileSystemTypes.PCloud;

    public bool SupportPassword =>
        Type is Job.Position.FileSystemTypes.Ftp or Job.Position.FileSystemTypes.Mega or Job.Position.FileSystemTypes.Nextcloud or
            Job.Position.FileSystemTypes.Sftp or Job.Position.FileSystemTypes.Smb or Job.Position.FileSystemTypes.MediaFire;

    public bool SupportOAuth =>
        Type is Job.Position.FileSystemTypes.Nextcloud or Job.Position.FileSystemTypes.DropBox or Job.Position.FileSystemTypes.OneDrive or
            Job.Position.FileSystemTypes.PCloud;


    public bool SupportOAuthServerUrl =>
        Type is Job.Position.FileSystemTypes.Nextcloud;
}