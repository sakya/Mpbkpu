using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Controls.Models;

public sealed class PositionDetailModel : INotifyPropertyChanged
{
    public enum FileSystemTypes
    {
        FileSystem,
        DropBox,
        Ftp,
        MediaFire,
        Mega,
        Nextcloud,
        OneDrive,
        Sftp,
        Smb
    }

    private FileSystemTypes _type;
    public FileSystemTypes Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value)) {
                OnPropertyChanged(nameof(IsLocalFileSystem));
                OnPropertyChanged(nameof(SupportUserPassword));
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

    public bool IsLocalFileSystem => Type == FileSystemTypes.FileSystem;

    public bool SupportCompression =>
        Type is FileSystemTypes.FileSystem or FileSystemTypes.Ftp or FileSystemTypes.Sftp or FileSystemTypes.Smb;

    public bool SupportUserPassword =>
        Type is FileSystemTypes.Ftp or FileSystemTypes.Mega or FileSystemTypes.Nextcloud or
            FileSystemTypes.Sftp or FileSystemTypes.Smb or FileSystemTypes.MediaFire;

    public bool SupportOAuth =>
        Type is FileSystemTypes.Nextcloud or FileSystemTypes.DropBox or FileSystemTypes.OneDrive;


    public bool SupportOAuthServerUrl =>
        Type is FileSystemTypes.Nextcloud;

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