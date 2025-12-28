using System;
using System.Runtime.InteropServices;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Utility functions for the cloud sync provider.
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// Adds a folder to the Windows Search Indexer.
        /// This is REQUIRED if the sync root is not under a standard user folder.
        /// </summary>
        /// <remarks>
        /// From the CloudMirror sample:
        /// "If the local (client) folder where the cloud file placeholders are created
        /// is not under the User folder (i.e. Documents, Photos, etc), then it is required
        /// to add the folder to the Search Indexer. This is because the properties for
        /// the cloud file state/progress are cached in the indexer, and if the folder isn't
        /// indexed, attempts to get the properties on items will not return the expected values."
        /// </remarks>
        public static void AddFolderToSearchIndexer(string folderPath)
        {
            try
            {
                var url = $"file:///{folderPath.Replace('\\', '/')}";
                System.Diagnostics.Debug.WriteLine($"Adding folder to search indexer: {url}");

                // Create the Search Manager
                var searchManagerType = Type.GetTypeFromCLSID(new Guid("7D096C5F-AC08-4F1F-BEB7-5C22C517CE39"));
                if (searchManagerType == null)
                {
                    System.Diagnostics.Debug.WriteLine("Could not get CSearchManager type");
                    return;
                }

                var searchManager = Activator.CreateInstance(searchManagerType) as ISearchManager;
                if (searchManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("Could not create CSearchManager instance");
                    return;
                }

                // Get the catalog manager
                searchManager.GetCatalog("SystemIndex", out var catalogManager);
                if (catalogManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("Could not get catalog manager");
                    return;
                }

                // Get the crawl scope manager
                catalogManager.GetCrawlScopeManager(out var crawlScopeManager);
                if (crawlScopeManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("Could not get crawl scope manager");
                    return;
                }

                // Add the folder as a default rule
                crawlScopeManager.AddDefaultScopeRule(url, 1, FOLLOW_FLAGS.FF_INDEXCOMPLEXURLS);
                crawlScopeManager.SaveAll();

                System.Diagnostics.Debug.WriteLine($"Successfully added folder to search indexer: {folderPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to add folder to search indexer: {ex.Message}");
                // Don't throw - this is not critical for basic operation
            }
        }

        #region Search API COM Interfaces

        [ComImport]
        [Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF69")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISearchManager
        {
            void GetIndexerVersionStr(out string ppszVersionString);
            void GetIndexerVersion(out uint pdwMajor, out uint pdwMinor);
            void GetParameter(string pszName, out object ppValue);
            void SetParameter(string pszName, ref object pValue);
            void get_ProxyName(out string ppszProxyName);
            void get_BypassList(out string ppszBypassList);
            void SetProxy(int sUseProxy, int fLocalByPassProxy, uint dwPortNumber, string pszProxyName, string pszBypassList);
            void GetCatalog(string pszCatalog, out ISearchCatalogManager ppCatalogManager);
            void get_UserAgent(out string ppszUserAgent);
            void put_UserAgent(string pszUserAgent);
            void get_UseProxy(out int pUseProxy);
            void get_LocalBypass(out int pfLocalBypass);
            void get_PortNumber(out uint pdwPortNumber);
        }

        [ComImport]
        [Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF50")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISearchCatalogManager
        {
            void get_Name(out string pszName);
            void GetParameter(string pszName, out object ppValue);
            void SetParameter(string pszName, ref object pValue);
            void GetStatus(out int pStatus, out int pPausedReason);
            void Reset();
            void Reindex();
            void ReindexMatchingURLs(string pszPattern);
            void ReindexSearchRoot(string pszRootURL);
            void put_ConnectTimeout(uint dwConnectTimeout);
            void get_ConnectTimeout(out uint pdwConnectTimeout);
            void put_DataTimeout(uint dwDataTimeout);
            void get_DataTimeout(out uint pdwDataTimeout);
            void NumberOfItems(out int plCount);
            void NumberOfItemsToIndex(out int plIncrementalCount, out int plNotificationQueue, out int plHighPriorityQueue);
            void URLBeingIndexed(out string pszUrl);
            void GetURLIndexingState(string pszURL, out uint pdwState);
            void GetPersistentItemsChangedSink(out object ppISearchPersistentItemsChangedSink);
            void RegisterViewForNotification(string pszView, object pViewChangedSink, out uint pdwCookie);
            void GetItemsChangedSink(object pISearchNotifyInlineSite, ref Guid riid, out object ppv, out Guid pGUIDCatalogResetSignature, out Guid pGUIDCheckPointSignature, out uint pdwLastCheckPointNumber);
            void UnregisterViewForNotification(uint dwCookie);
            void SetExtensionClusion(string pszExtension, int fExclude);
            void GetQueryHelper(out object ppSearchQueryHelper);
            void put_DiacriticSensitivity(int fDiacriticSensitive);
            void get_DiacriticSensitivity(out int pfDiacriticSensitive);
            void GetCrawlScopeManager(out ISearchCrawlScopeManager ppCrawlScopeManager);
        }

        [ComImport]
        [Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF55")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISearchCrawlScopeManager
        {
            void AddDefaultScopeRule(string pszUrl, int fInclude, FOLLOW_FLAGS fFollowFlags);
            void AddRoot(object pSearchRoot);
            void RemoveRoot(string pszUrl);
            void GetEnumerationRoots(out object ppSearchRoots);
            void AddHierarchicalScope(string pszUrl, int fInclude, int fDefault, int fOverrideChildren);
            void AddUserScopeRule(string pszUrl, int fInclude, int fOverrideChildren, uint fFollowFlags);
            void RemoveScopeRule(string pszUrl);
            void GetEnumerationScopes(int fInclude, out object ppSearchScopeEnumerator);
            void HasParentScopeRule(string pszUrl, out int pfHasParent);
            void HasChildScopeRule(string pszUrl, out int pfHasChild);
            void IncludedInCrawlScope(string pszUrl, out int pfIsIncluded);
            void IncludedInCrawlScopeEx(string pszUrl, out int pfIsIncluded, out int pfReasonForInclusion);
            void RevertToDefaultScopes();
            void SaveAll();
            void GetParentScopeVersionId(string pszUrl, out int plScopeId);
            void RemoveDefaultScopeRule(string pszUrl);
        }

        private enum FOLLOW_FLAGS
        {
            FF_INDEXCOMPLEXURLS = 1,
            FF_SUPPRESSINDEXING = 2
        }

        #endregion
    }
}
