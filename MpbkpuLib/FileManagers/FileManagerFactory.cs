using MpbkpuLib.Interfaces;
using System.Text.RegularExpressions;

namespace MpbkpuLib.FileManagers
{
    public static class FileManagerFactory
    {
        /// <summary>
        /// Create a <see cref="IFileManager"/> based on a absolute <see cref="path"/>
        /// C:\path
        /// /path
        /// ftp://user:password@host/position
        /// sftp://user:password@host/position
        /// sftp://user:keyfile=pathToFile@host/position
        /// smb://domain\user:password@host\position
        /// onedrive://user/position
        /// mega://user:password/position
        /// dropbox://user/position
        /// mediafire://user:password/position
        /// </summary>
        /// <param name="path">The path</param>
        /// <param name="newPosition">The position computed from the <see cref="Path"/> (i.e. without username/password)</param>
        /// <param name="user">The user to use to access to the file system</param>
        /// <param name="password">The password to use to access the file system</param>
        /// <param name="oauth2Settings">The <see cref="OAuth2Settings"/></param>
        /// <returns>A <see cref="IFileManager"/></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public static IFileManager GetFileManager(string? path, out string newPosition, string? user = null, string? password = null, OAuth2Settings? oauth2Settings = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            newPosition = path;
            IFileManager? res = null;
            // FTP
            // ftp://user:password@host/position
            // (FTP and SFTP also accept scheme:///host/position, the form written by the GUI)
            if (path.StartsWith(Ftp.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{Ftp.Scheme}/?(([^:/]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var ftpFm = new Ftp();
                        if (!string.IsNullOrEmpty(m.Groups[2].Value) && !string.IsNullOrEmpty(m.Groups[3].Value))
                            ftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        else if (!string.IsNullOrEmpty(user))
                            ftpFm.Credentials = new System.Net.NetworkCredential(user, password);

                        ftpFm.Host = m.Groups[4].Value;
                        if (!string.IsNullOrEmpty(m.Groups[5].Value))
                            ftpFm.Port = int.Parse(m.Groups[5].Value.Remove(0, 1));
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = m.Groups[6].Value;
                        else
                            newPosition = "/";
                        res = ftpFm;
                    } else
                        throw new Exception("Invalid address");
                } else {
                    throw new Exception("Invalid address");
                }
            }

            // SFTP
            // sftp://user:password@host/position
            // sftp://user:keyfile=pathToFile@host/position
            if (res == null && path.StartsWith(Sftp.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{Sftp.Scheme}/?(([^:/]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var sftpFm = new Sftp();
                        if (!string.IsNullOrEmpty(m.Groups[2].Value) && !string.IsNullOrEmpty(m.Groups[3].Value))
                            sftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        else if (!string.IsNullOrEmpty(user))
                            sftpFm.Credentials = new System.Net.NetworkCredential(user, password);

                        sftpFm.Host = m.Groups[4].Value;
                        if (sftpFm.Credentials?.Password?.StartsWith("keyfile=") == true) {
                            sftpFm.PrivateKeyFile = sftpFm.Credentials.Password.Substring(8);
                            sftpFm.Credentials.Password = string.Empty;
                        }
                        if (!string.IsNullOrEmpty(m.Groups[5].Value))
                            sftpFm.Port = int.Parse(m.Groups[5].Value.Remove(0, 1));
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = m.Groups[6].Value;
                        else
                            newPosition = "/";
                        res = sftpFm;
                    } else
                        throw new Exception("Invalid address");
                } else {
                    throw new Exception("Invalid address");
                }
            }

            // SMB
            // smb://domain\user:password@host/share/position (the domain is optional)
            // (also smb:///host/share/position, the form written by the GUI)
            if (res == null && path.StartsWith(Smb.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{Smb.Scheme}/?((([^\\\\/:@]+)\\\\)?([^\\\\/:@]+):([^@]+)@)?([^/]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 8) {
                        Smb? smbFm;
                        var host = m.Groups[6].Value;
                        if (!string.IsNullOrEmpty(m.Groups[4].Value) && !string.IsNullOrEmpty(m.Groups[5].Value))
                            smbFm = new Smb(m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value, host);
                        else if (!string.IsNullOrEmpty(user)) {
                            // domain\user
                            var idx = user.IndexOf('\\');
                            smbFm = idx >= 0
                                ? new Smb(user[..idx], user[(idx + 1)..], password ?? string.Empty, host)
                                : new Smb(string.Empty, user, password ?? string.Empty, host);
                        } else
                            throw new Exception("Invalid address");

                        if (!string.IsNullOrEmpty(m.Groups[7].Value))
                            newPosition = $"{Smb.Scheme}{smbFm.Host}{m.Groups[7].Value}";
                        else
                            newPosition = $"{Smb.Scheme}{smbFm.Host}/";
                        res = smbFm;
                    } else
                        throw new Exception("Invalid address");
                } else {
                    throw new Exception("Invalid address");
                }
            }

            // OneDrive
            // onedrive://account@live.com/position
            if (res == null && path.StartsWith(OneDrive.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{OneDrive.Scheme}([^/]+)/(.*)$");
                if (m.Success) {
                    string? account = null;
                    if (m.Groups.Count == 3) {
                        account = m.Groups[1].Value;
                    }
                    if (!string.IsNullOrEmpty(m.Groups[2].Value))
                        newPosition = $"{OneDrive.Scheme}{m.Groups[2].Value}";
                    else
                        newPosition = $"{OneDrive.Scheme}/";
                    res = new OneDrive(account);
                } else {
                    if (!string.IsNullOrEmpty(user)) {
                        newPosition = path.Substring(OneDrive.Scheme.Length);
                        res = new OneDrive(user);
                    } else {
                        throw new Exception("Invalid address");
                    }
                }
            }

            // MEGA
            // mega://user:password/position
            if (res == null && path.StartsWith(Mega.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{Mega.Scheme}(([^:]+):([^/]+))/?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 5) {
                        var megaFm = new Mega(m.Groups[2].Value, m.Groups[3].Value);
                        if (!string.IsNullOrEmpty(m.Groups[4].Value))
                            newPosition = $"{Mega.Scheme}{m.Groups[4].Value}";
                        else
                            newPosition = $"{Mega.Scheme}/";
                        res = megaFm;
                    } else
                        throw new Exception("Invalid address");
                } else {
                    if (!string.IsNullOrEmpty(user)) {
                        newPosition = path.Substring(Mega.Scheme.Length);
                        res = new Mega(user, password ?? string.Empty);
                    } else {
                        throw new Exception("Invalid address");
                    }
                }
            }

            // Dropbox
            // dropbox://user/position
            if (res == null && path.StartsWith(Dropbox.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{Dropbox.Scheme}([^/]+)/(.*)$");
                if (m.Success) {
                    string? account = null;
                    if (m.Groups.Count == 3) {
                        account = m.Groups[1].Value;
                    }
                    if (!string.IsNullOrEmpty(m.Groups[2].Value))
                        newPosition = $"{Dropbox.Scheme}{m.Groups[2].Value}";
                    else
                        newPosition = $"{Dropbox.Scheme}/";
                    res = new Dropbox(account);
                } else {
                    if (!string.IsNullOrEmpty(user)) {
                        newPosition = path.Substring(Dropbox.Scheme.Length);
                        res = new Dropbox(user);
                    } else {
                        throw new Exception("Invalid address");
                    }
                }
            }

            // MediaFire
            // mediafire://user:password/position
            if (res == null && path.StartsWith(MediaFire.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{MediaFire.Scheme}(([^:]+):([^/]+))/?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 5) {
                        var megaFm = new MediaFire(m.Groups[2].Value, m.Groups[3].Value);
                        if (!string.IsNullOrEmpty(m.Groups[4].Value))
                            newPosition = $"{MediaFire.Scheme}{m.Groups[4].Value}";
                        else
                            newPosition = $"{MediaFire.Scheme}/";
                        res = megaFm;
                    } else
                        throw new Exception("Invalid address");
                } else {
                    if (!string.IsNullOrEmpty(user)) {
                        newPosition = path.Substring(MediaFire.Scheme.Length);
                        res = new MediaFire(user, password ?? string.Empty);
                    } else {
                        throw new Exception("Invalid address");
                    }
                }
            }

            // Nextcloud
            // nextcloud://userId/position
            if (res == null && path.StartsWith(Nextcloud.Scheme, StringComparison.InvariantCultureIgnoreCase)) {
                var m = Regex.Match(path, $"{Nextcloud.Scheme}([^/]+)/(.*)$");
                if (m.Success) {
                    string? account = null;
                    if (m.Groups.Count == 3) {
                        account = m.Groups[1].Value;
                    }
                    if (!string.IsNullOrEmpty(m.Groups[2].Value))
                        newPosition = $"{Nextcloud.Scheme}{m.Groups[2].Value}";
                    else
                        newPosition = $"{Nextcloud.Scheme}/";
                    res = new Nextcloud(account, password);
                } else {
                    if (!string.IsNullOrEmpty(user)) {
                        newPosition = path.Substring(Nextcloud.Scheme.Length);
                        res = new Nextcloud(user, password);
                    } else {
                        throw new Exception("Invalid address");
                    }
                }
            }

            // Default to FileSystem
            if (res == null) {
                var m = Regex.Match(path, "^[a-zA-Z]:[\\\\/]");
                // Paths without a drive letter (relative or UNC) use the file system of the current OS
                res = m.Success || OperatingSystem.IsWindows() ? new LocalFileSystemWindows() : new LocalFileSystemLinux();
            }

            if (oauth2Settings is { IsEmpty: false } && res is not IOAuth2)
                throw new Exception($"{res.GetType().Name} does not support OAuth2");

            if (oauth2Settings is { IsEmpty: false } && res is IOAuth2 oaFm) {
                oaFm.AppName = oauth2Settings.AppName;
                oaFm.AppId = oauth2Settings.AppId;
                oaFm.AppSecret = oauth2Settings.AppSecret;
                oaFm.TenantId = oauth2Settings.TenantId;
                oaFm.ServerUrl = oauth2Settings.ServerUrl;
                oaFm.RedirectUrl = oauth2Settings.RedirectUrl;
            }

            return res;
        } // GetFileManager
    } // FileManagerFactory
}