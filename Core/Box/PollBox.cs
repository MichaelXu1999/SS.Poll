﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using Datory;
using SS.Poll.Core.Box.Atom.Atom.AdditionalElements;
using SS.Poll.Core.Box.Atom.Atom.AdditionalElements.DublinCore;
using SS.Poll.Core.Box.Atom.Atom.Core;
using SS.Poll.Core.Models;
using SS.Poll.Core.Utils;

namespace SS.Poll.Core.Box
{
    public static class PollBox
    {
        private const string VersionFileName = "version.txt";

        

        public static bool IsHistoric(string directoryPath)
        {
            if (!PollUtils.IsFileExists(PollUtils.PathCombine(directoryPath, VersionFileName))) return true;

            PollUtils.DeleteFileIfExists(PollUtils.PathCombine(directoryPath, VersionFileName));

            return false;
        }

        public static void ImportPoll(int siteId, string directoryPath, bool overwrite)
        {
            if (!Directory.Exists(directoryPath)) return;
            var isHistoric = IsHistoric(directoryPath);

            var filePaths = Directory.GetFiles(directoryPath);

            foreach (var filePath in filePaths)
            {
                var feed = AtomFeed.Load(new FileStream(filePath, FileMode.Open));

                var pollInfo = new PollInfo();

                foreach (var tableColumn in PollManager.Repository.TableColumns)
                {
                    var value = GetValue(feed.AdditionalElements, tableColumn);
                    pollInfo.Set(tableColumn.AttributeName, value);
                }

                pollInfo.SiteId = siteId;

                if (isHistoric)
                {
                    pollInfo.Title = GetDcElementContent(feed.AdditionalElements, "InputName");
                }

                var srcPollInfo = PollManager.GetPollInfo(siteId, pollInfo.Title);
                if (srcPollInfo != null)
                {
                    if (overwrite)
                    {
                        PollManager.Repository.Delete(siteId, srcPollInfo.Id);
                    }
                    else
                    {
                        pollInfo.Title = PollManager.Repository.GetImportTitle(siteId, pollInfo.Title);
                    }
                }

                pollInfo.Id = PollManager.Repository.Insert(pollInfo);

                var directoryName = GetDcElementContent(feed.AdditionalElements, "Id");
                if (isHistoric)
                {
                    directoryName = GetDcElementContent(feed.AdditionalElements, "InputID");
                }
                var titleAttributeNameDict = new NameValueCollection();
                if (!string.IsNullOrEmpty(directoryName))
                {
                    var fieldDirectoryPath = PollUtils.PathCombine(directoryPath, directoryName);
                    titleAttributeNameDict = ImportFields(siteId, pollInfo.Id, fieldDirectoryPath, isHistoric);
                }

                foreach (AtomEntry entry in feed.Entries)
                {
                    var logInfo = new LogInfo();

                    foreach (var tableColumn in LogManager.Repository.TableColumns)
                    {
                        var value = GetValue(entry.AdditionalElements, tableColumn);
                        logInfo.Set(tableColumn.AttributeName, value);
                    }

                    var attributes = GetDcElementNameValueCollection(entry.AdditionalElements);
                    foreach (string entryName in attributes.Keys)
                    {
                        logInfo.Set(entryName, attributes[entryName]);
                    }

                    if (isHistoric)
                    {
                        foreach (var title in titleAttributeNameDict.AllKeys)
                        {
                            logInfo.Set(title, logInfo.Get(titleAttributeNameDict[title]));
                        }

                        logInfo.AddDate = PollUtils.ToDateTime(GetDcElementContent(entry.AdditionalElements, "adddate"));
                    }

                    LogManager.Repository.Insert(pollInfo, logInfo);
                }
            }
        }

        public static NameValueCollection ImportFields(int siteId, int pollId, string styleDirectoryPath, bool isHistoric)
        {
            var titleAttributeNameDict = new NameValueCollection();

            if (!Directory.Exists(styleDirectoryPath)) return titleAttributeNameDict;

            var filePaths = Directory.GetFiles(styleDirectoryPath);
            foreach (var filePath in filePaths)
            {
                var feed = AtomFeed.Load(new FileStream(filePath, FileMode.Open));

                var title = GetDcElementContent(feed.AdditionalElements, nameof(FieldInfo.Title));
                if (isHistoric)
                {
                    var attributeName = GetDcElementContent(feed.AdditionalElements, "AttributeName");
                    title = GetDcElementContent(feed.AdditionalElements, "DisplayName");

                    titleAttributeNameDict[title] = attributeName;
                }
                var fieldType = GetDcElementContent(feed.AdditionalElements, nameof(FieldInfo.FieldType));
                if (isHistoric)
                {
                    fieldType = GetDcElementContent(feed.AdditionalElements, "InputType");
                }
                var taxis = PollUtils.ToInt(GetDcElementContent(feed.AdditionalElements, "Taxis"), 0);

                var fieldInfo = new FieldInfo
                {
                    PollId = pollId,
                    Taxis = taxis,
                    Title = title,
                    FieldType = fieldType
                };

                var fieldItems = new List<FieldItemInfo>();
                foreach (AtomEntry entry in feed.Entries)
                {
                    var itemValue = GetDcElementContent(entry.AdditionalElements, "ItemValue");
                    var isSelected = PollUtils.ToBool(GetDcElementContent(entry.AdditionalElements, "IsSelected"), false);

                    fieldItems.Add(new FieldItemInfo
                    {
                        PollId = pollId,
                        FieldId = 0,
                        Value = itemValue,
                        IsSelected = isSelected
                    });
                }

                if (fieldItems.Count > 0)
                {
                    fieldInfo.Items = fieldItems;
                }

                if (FieldManager.Repository.IsTitleExists(pollId, title))
                {
                    FieldManager.Repository.Delete(pollId, title);
                }
                FieldManager.Repository.Insert(siteId, fieldInfo);
            }

            return titleAttributeNameDict;
        }

        public static void ExportPoll(int siteId, string directoryPath, int pollId)
        {
            var pollInfo = PollManager.GetPollInfo(siteId, pollId);
            var filePath = PollUtils.PathCombine(directoryPath, pollInfo.Id + ".xml");

            var feed = GetEmptyFeed();

            foreach (var tableColumn in PollManager.Repository.TableColumns)
            {
                SetValue(feed.AdditionalElements, tableColumn, pollInfo);
            }

            var styleDirectoryPath = PollUtils.PathCombine(directoryPath, pollInfo.Id.ToString());
            ExportFields(pollInfo.Id, styleDirectoryPath);

            var logInfoList = LogManager.Repository.GetLogInfoList(pollInfo.Id, 0, 0);
            foreach (var logInfo in logInfoList)
            {
                var entry = GetAtomEntry(logInfo);
                feed.Entries.Add(entry);
            }
            feed.Save(filePath);
        }

        private static AtomFeed ExportFieldInfo(FieldInfo fieldInfo)
        {
            var feed = GetEmptyFeed();

            foreach (var tableColumn in FieldManager.Repository.TableColumns)
            {
                SetValue(feed.AdditionalElements, tableColumn, fieldInfo);
            }

            return feed;
        }

        public static void ExportFields(int pollId, string styleDirectoryPath)
        {

            PollUtils.DeleteDirectoryIfExists(styleDirectoryPath);
            PollUtils.CreateDirectoryIfNotExists(styleDirectoryPath);

            var fieldInfoList = FieldManager.GetFieldInfoList(pollId);
            foreach (var fieldInfo in fieldInfoList)
            {
                var filePath = PollUtils.PathCombine(styleDirectoryPath, fieldInfo.Id + ".xml");
                var feed = ExportFieldInfo(fieldInfo);
                if (fieldInfo.Items != null && fieldInfo.Items.Count > 0)
                {
                    foreach (var itemInfo in fieldInfo.Items)
                    {
                        var entry = ExportTableStyleItemInfo(itemInfo);
                        feed.Entries.Add(entry);
                    }
                }
                feed.Save(filePath);
            }
        }

        private static AtomEntry ExportTableStyleItemInfo(FieldItemInfo styleItemInfo)
        {
            var entry = GetEmptyEntry();

            foreach (var tableColumn in FieldManager.ItemRepository.TableColumns)
            {
                SetValue(entry.AdditionalElements, tableColumn, styleItemInfo);
            }

            return entry;
        }

        private const string Prefix = "SiteServer_";

        private static string ToXmlContent(string inputString)
        {
            var contentBuilder = new StringBuilder(inputString);
            contentBuilder.Replace("<![CDATA[", string.Empty);
            contentBuilder.Replace("]]>", string.Empty);
            contentBuilder.Insert(0, "<![CDATA[");
            contentBuilder.Append("]]>");
            return contentBuilder.ToString();
        }

        private static void AddDcElement(ScopedElementCollection collection, string name, string content)
        {
            if (!string.IsNullOrEmpty(content))
            {
                collection.Add(new DcElement(Prefix + name, ToXmlContent(content)));
            }
        }

        public static void AddDcElement(ScopedElementCollection collection, List<string> nameList, string content)
        {
            if (!string.IsNullOrEmpty(content))
            {
                foreach (var name in nameList)
                {
                    collection.Add(new DcElement(Prefix + name, ToXmlContent(content)));
                }
            }
        }

        public static string GetDcElementContent(ScopedElementCollection additionalElements, List<string> nameList)
        {
            return GetDcElementContent(additionalElements, nameList, "");
        }

        private static string GetDcElementContent(ScopedElementCollection additionalElements, string name, string defaultContent = "")
        {
            var localName = Prefix + name;
            var element = additionalElements.FindScopedElementByLocalName(localName);
            return element != null ? element.Content : defaultContent;
        }

        private static string GetDcElementContent(ScopedElementCollection additionalElements, List<string> nameList, string defaultContent)
        {
            foreach (var name in nameList)
            {
                var localName = Prefix + name;
                var element = additionalElements.FindScopedElementByLocalName(localName);
                if (element == null) continue;

                return element.Content;
            }
            return defaultContent;
        }

        private static NameValueCollection GetDcElementNameValueCollection(ScopedElementCollection additionalElements)
        {
            return additionalElements.GetNameValueCollection(Prefix);
        }

        private static AtomFeed GetEmptyFeed()
        {
            var feed = new AtomFeed
            {
                Title = new AtomContentConstruct("title", "siteserver channel"),
                Author = new AtomPersonConstruct("author",
                    "siteserver", new Uri("http://www.siteserver.cn")),
                Modified = new AtomDateConstruct("modified", DateTime.Now,
                    TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now))
            };

            return feed;
        }

        private static AtomEntry GetEmptyEntry()
        {
            var entry = new AtomEntry
            {
                Id = new Uri("http://www.siteserver.cn/"),
                Title = new AtomContentConstruct("title", "title"),
                Modified = new AtomDateConstruct("modified", DateTime.Now,
                    TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now)),
                Issued = new AtomDateConstruct("issued", DateTime.Now,
                    TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now))
            };

            return entry;
        }

        private static string Encrypt(string inputString)
        {
            if (string.IsNullOrEmpty(inputString)) return string.Empty;

            var encryptor = new DesEncryptor
            {
                InputString = inputString,
                EncryptKey = "TgQQk42O"
            };
            encryptor.DesEncrypt();
            return encryptor.OutString;
        }


        private static string Decrypt(string inputString)
        {
            if (string.IsNullOrEmpty(inputString)) return string.Empty;

            var encryptor = new DesEncryptor
            {
                InputString = inputString,
                DecryptKey = "TgQQk42O"
            };
            encryptor.DesDecrypt();
            return encryptor.OutString;
        }

        private static AtomEntry GetAtomEntry(Entity entity)
        {
            var entry = GetEmptyEntry();

            foreach (var keyValuePair in entity.ToDictionary())
            {
                if (keyValuePair.Value != null)
                {
                    AddDcElement(entry.AdditionalElements, keyValuePair.Key, keyValuePair.Value.ToString());
                }
            }

            return entry;
        }

        private static object GetValue(ScopedElementCollection additionalElements, TableColumn tableColumn)
        {
            if (tableColumn.DataType == DataType.Boolean)
            {
                return PollUtils.ToBool(GetDcElementContent(additionalElements, tableColumn.AttributeName), false);
            }
            if (tableColumn.DataType == DataType.DateTime)
            {
                return PollUtils.ToDateTime(GetDcElementContent(additionalElements, tableColumn.AttributeName));
            }
            if (tableColumn.DataType == DataType.Decimal)
            {
                return PollUtils.ToDecimal(GetDcElementContent(additionalElements, tableColumn.AttributeName), 0);
            }
            if (tableColumn.DataType == DataType.Integer)
            {
                return PollUtils.ToInt(GetDcElementContent(additionalElements, tableColumn.AttributeName), 0);
            }
            if (tableColumn.DataType == DataType.Text)
            {
                return Decrypt(GetDcElementContent(additionalElements, tableColumn.AttributeName));
            }
            return GetDcElementContent(additionalElements, tableColumn.AttributeName);
        }

        private static void SetValue(ScopedElementCollection additionalElements, TableColumn tableColumn, Entity entity)
        {
            var value = entity.Get(tableColumn.AttributeName)?.ToString();
            if (tableColumn.DataType == DataType.Text)
            {
                value = Encrypt(value);
            }
            AddDcElement(additionalElements, tableColumn.AttributeName, value);
        }
    }
}
