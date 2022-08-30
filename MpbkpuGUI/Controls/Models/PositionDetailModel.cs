using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Controls.Models;

public sealed class PositionDetailModel : INotifyPropertyChanged
{
    private Job.Position.FileSystemTypes _type;
    public Job.Position.FileSystemTypes Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value)) {
                OnPropertyChanged(nameof(IsLocalFileSystem));
                OnPropertyChanged(nameof(SupportUser));
                OnPropertyChanged(nameof(SupportPassword));
                OnPropertyChanged(nameof(SupportOAuth));
                OnPropertyChanged(nameof(SupportOAuthServerUrl));
                OnPropertyChanged(nameof(SupportCompression));
            }
        }
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

    public bool SupportCompression =>
        Type is Job.Position.FileSystemTypes.FileSystem or Job.Position.FileSystemTypes.Ftp or Job.Position.FileSystemTypes.Sftp or Job.Position.FileSystemTypes.Smb;

    public bool SupportUser =>
        Type is Job.Position.FileSystemTypes.Ftp or Job.Position.FileSystemTypes.Mega or Job.Position.FileSystemTypes.Nextcloud or
            Job.Position.FileSystemTypes.Sftp or Job.Position.FileSystemTypes.Smb or Job.Position.FileSystemTypes.MediaFire
            or Job.Position.FileSystemTypes.OneDrive or Job.Position.FileSystemTypes.DropBox;

    public bool SupportPassword =>
        Type is Job.Position.FileSystemTypes.Ftp or Job.Position.FileSystemTypes.Mega or Job.Position.FileSystemTypes.Nextcloud or
            Job.Position.FileSystemTypes.Sftp or Job.Position.FileSystemTypes.Smb or Job.Position.FileSystemTypes.MediaFire;

    public bool SupportOAuth =>
        Type is Job.Position.FileSystemTypes.Nextcloud or Job.Position.FileSystemTypes.DropBox or Job.Position.FileSystemTypes.OneDrive;


    public bool SupportOAuthServerUrl =>
        Type is Job.Position.FileSystemTypes.Nextcloud;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}