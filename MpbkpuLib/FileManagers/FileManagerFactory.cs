using MpbkpuLib.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        /// onedrive://accessToken:refreshToken:position
        /// </summary>
        /// <param name="path">The path</param>
        /// <param name="newPosition">The position computed from the <see cref="Path"/></param>
        /// <returns>A <see cref="IFileManager"/></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public static IFileManager GetFileManager(string? path, out string newPosition)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            newPosition = path;
            // FTP
            // ftp://user:password@host/position
            if (path.StartsWith("ftp://")) {
                var m = Regex.Match(path, "ftp://(([^:]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var ftpFm = new Ftp();
                        ftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
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
                } else
                    throw new Exception("Invalid address");
            }

            // SFTP
            // sftp://user:password@host/position
            // sftp://user:keyfile=pathToFile@host/position
            if (path.StartsWith("sftp://")) {
                var m = Regex.Match(path, "sftp://(([^:]+):([^@]+)@)?([^/:]+)(:[0-9]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var sftpFm = new Sftp();
                        sftpFm.Credentials = new System.Net.NetworkCredential(m.Groups[2].Value, m.Groups[3].Value);
                        sftpFm.Host = m.Groups[4].Value;
                        if (sftpFm.Credentials.Password.StartsWith("keyfile=")) {
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
                } else
                    throw new Exception("Invalid address");
            }

            // SMB
            // smb://domain\user:password@host\position
            if (path.StartsWith("smb://")) {
                var m = Regex.Match(path, "smb://((.+)?\\\\(.+):(.+)@)?([^/]+)?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 7) {
                        var smbFm = new Smb(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value);
                        if (!string.IsNullOrEmpty(m.Groups[6].Value))
                            newPosition = $"smb://{smbFm.Host}{m.Groups[6].Value}";
                        else
                            newPosition = $"smb://{smbFm.Host}/";
                        return smbFm;
                    } else
                        throw new Exception("Invalid address");
                } else
                    throw new Exception("Invalid address");
            }

            // OneDrive
            // onedrive://account@live.com/position
            if (path.StartsWith("onedrive://")) {
                var m = Regex.Match(path, "onedrive://([^/]+)/(.*)$");
                if (m.Success) {
                    string? account = null;
                    if (m.Groups.Count == 3) {
                        account = m.Groups[1].Value;
                    }
                    if (!string.IsNullOrEmpty(m.Groups[2].Value))
                        newPosition = $"onedrive://{m.Groups[2].Value}";
                    else
                        newPosition = "onedrive:///";
                    var odFm = new OneDrive(account);
                    return odFm;
                } else
                    throw new Exception("Invalid address");
            }

            // MEGA
            // mega://user:password/position
            if (path.StartsWith("mega://")) {
                var m = Regex.Match(path, "mega://(([^:]+):([^/]+))/?(.*)?$");
                if (m.Success) {
                    if (m.Groups.Count == 5) {
                        var megaFm = new Mega(m.Groups[2].Value, m.Groups[3].Value);
                        if (!string.IsNullOrEmpty(m.Groups[4].Value))
                            newPosition = $"mega://{m.Groups[4].Value}";
                        else
                            newPosition = $"mega:///";
                        return megaFm;
                    } else
                        throw new Exception("Invalid address");
                } else
                    throw new Exception("Invalid address");
            }

            // Dropbox
            // dropbox://user/position
            if (path.StartsWith("dropbox://")) {
                var m = Regex.Match(path, "dropbox://([^/]+)/(.*)$");
                if (m.Success) {
                    string? account = null;
                    if (m.Groups.Count == 3) {
                        account = m.Groups[1].Value;
                    }
                    if (!string.IsNullOrEmpty(m.Groups[2].Value))
                        newPosition = $"dropbox://{m.Groups[2].Value}";
                    else
                        newPosition = "dropbox:///";
                    var odFm = new Dropbox(account);
                    return odFm;
                } else
                    throw new Exception("Invalid address");
            }

            // Default to FileSystem
            return new FileSystem();
        } // GetFileManager
    } // FileManagerFactory
}