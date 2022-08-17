using MpbkpuLib.Interfaces;
using System.Text.RegularExpressions;

namespace MpbkpuLib.FileManagers
{
    public static class FileManagerFactory
    {
        /// <summary>
        /// Create a <see cref="IFileManager"/> based on the <see cref="path"/>
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
        /// <param name="newPosition">The position computed from the <see cref="Path"/></param>
        /// <param name="user">The user to use to access to the file system</param>
        /// <param name="password">The password to use to access the file system</param>
        /// <returns>A <see cref="IFileManager"/></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public static IFileManager GetFileManager(string? path, out string newPosition, string? user = null, string? password = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            newPosition = path;
            // FTP
            // ftp://user:password@host/position
            if (path.StartsWith(Ftp.Scheme)) {
                var m = Regex.Match(path, $"{Ftp.Scheme}(([^:]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var ftpFm = new Ftp();
                        if (!string.IsNullOrEmpty(m.Groups[2].Value) && !string.IsNullOrEmpty(m.Groups[3].Value))
                            ftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        else if (string.IsNullOrEmpty(user))
                            ftpFm.Credentials = new System.Net.NetworkCredential(user, password);

                        ftpFm.Host = m.Groups[4].Value;
                        if (!string.IsNullOrEmpty(m.Groups[5].Value))
                            ftpFm.Port = int.Parse(m.Groups[5].Value.Remove(0, 1));
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = m.Groups[6].Value;
                        else
                            newPosition = "/";
                        return ftpFm;
                    } else
                        throw new Exception("Invalid address");
                }
                throw new Exception("Invalid address");
            }

            // SFTP
            // sftp://user:password@host/position
            // sftp://user:keyfile=pathToFile@host/position
            if (path.StartsWith(Sftp.Scheme)) {
                var m = Regex.Match(path, $"{Sftp.Scheme}(([^:]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var sftpFm = new Sftp();
                        if (!string.IsNullOrEmpty(m.Groups[2].Value) && !string.IsNullOrEmpty(m.Groups[3].Value))
                            sftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        else if (string.IsNullOrEmpty(user))
                            sftpFm.Credentials = new System.Net.NetworkCredential(user, password);

                        sftpFm.Host = m.Groups[4].Value;
                        if (sftpFm.Credentials != null && sftpFm.Credentials.Password.StartsWith("keyfile=")) {
                            sftpFm.PrivateKeyFile = sftpFm.Credentials.Password.Substring(8);
                            sftpFm.Credentials.Password = string.Empty;
                        }
                        if (!string.IsNullOrEmpty(m.Groups[5].Value))
                            sftpFm.Port = int.Parse(m.Groups[5].Value.Remove(0, 1));
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = m.Groups[6].Value;
                        else
                            newPosition = "/";
                        return sftpFm;
                    } else
                        throw new Exception("Invalid address");
                }
                throw new Exception("Invalid address");
            }

            // SMB
            // smb://domain\user:password@host\position
            if (path.StartsWith(Smb.Scheme)) {
                var m = Regex.Match(path, $"{Smb.Scheme}((.+)?\\\\(.+):(.+)@)?([^/]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        Smb? smbFm = null;
                        if (!string.IsNullOrEmpty(m.Groups[2].Value) && !string.IsNullOrEmpty(m.Groups[3].Value) && !string.IsNullOrEmpty(m.Groups[3].Value))
                            smbFm = new Smb(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value);
                        else if (!string.IsNullOrEmpty(user)) {
                            // Get domain
                            if (user.Contains("\\")) {
                                var idx = user.IndexOf("\\", StringComparison.InvariantCultureIgnoreCase);
                                var domain = user.Substring(idx);
                                smbFm = new Smb(domain, user.Substring(idx + 1), password ?? string.Empty, m.Groups[5].Value);
                            } else {
                                smbFm = new Smb(string.Empty, user, password ?? string.Empty, m.Groups[5].Value);
                            }
                        } else
                            throw new Exception("Invalid address");

                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = $"{Smb.Scheme}{smbFm.Host}{m.Groups[6].Value}";
                        else
                            newPosition = $"{Smb.Scheme}{smbFm.Host}/";
                        return smbFm;
                    } else
                        throw new Exception("Invalid address");
                }

                throw new Exception("Invalid address");
            }

            // OneDrive
            // onedrive://account@live.com/position
            if (path.StartsWith(OneDrive.Scheme)) {
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
                    return new OneDrive(account);
                }

                if (!string.IsNullOrEmpty(user)) {
                    newPosition = path.Substring(OneDrive.Scheme.Length);
                    return new OneDrive(user);
                }
                throw new Exception("Invalid address");
            }

            // MEGA
            // mega://user:password/position
            if (path.StartsWith(Mega.Scheme)) {
                var m = Regex.Match(path, $"{Mega.Scheme}(([^:]+):([^/]+))/?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 5) {
                        var megaFm = new Mega(m.Groups[2].Value, m.Groups[3].Value);
                        if (!string.IsNullOrEmpty(m.Groups[4].Value))
                            newPosition = $"{Mega.Scheme}{m.Groups[4].Value}";
                        else
                            newPosition = $"{Mega.Scheme}/";
                        return megaFm;
                    } else
                        throw new Exception("Invalid address");
                }

                if (!string.IsNullOrEmpty(user)) {
                    newPosition = path.Substring(Mega.Scheme.Length);
                    return new Mega(user, password ?? string.Empty);
                }
                throw new Exception("Invalid address");
            }

            // Dropbox
            // dropbox://user/position
            if (path.StartsWith(Dropbox.Scheme)) {
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
                    return new Dropbox(account);
                }

                if (!string.IsNullOrEmpty(user)) {
                    newPosition = path.Substring(Dropbox.Scheme.Length);
                    return new Dropbox(user);
                }
                throw new Exception("Invalid address");
            }

            // MediaFire
            // mediafire://user:password/position
            if (path.StartsWith(MediaFire.Scheme)) {
                var m = Regex.Match(path, $"{MediaFire.Scheme}(([^:]+):([^/]+))/?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 5) {
                        var megaFm = new MediaFire(m.Groups[2].Value, m.Groups[3].Value);
                        if (!string.IsNullOrEmpty(m.Groups[4].Value))
                            newPosition = $"{MediaFire.Scheme}{m.Groups[4].Value}";
                        else
                            newPosition = $"{MediaFire.Scheme}/";
                        return megaFm;
                    } else
                        throw new Exception("Invalid address");
                }

                if (!string.IsNullOrEmpty(user)) {
                    newPosition = path.Substring(MediaFire.Scheme.Length);
                    return new MediaFire(user, password ?? string.Empty);
                }
                throw new Exception("Invalid address");
            }

            // Default to FileSystem
            return new FileSystem();
        } // GetFileManager
    } // FileManagerFactory
}