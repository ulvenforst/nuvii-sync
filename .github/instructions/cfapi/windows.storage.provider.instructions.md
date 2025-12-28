Classes
Name	Description
CachedFileUpdater	
Manages files so that they can be updated in real-time by an app that participates in the Cached File Updater contract.

CachedFileUpdaterUI	
Used to interact with the file picker if your app provides file updates through the Cached File Updater contract.

FileUpdateRequest	
Provides information about a requested file update so that the app can complete the request.

FileUpdateRequestDeferral	
Use to complete an update asynchronously.

FileUpdateRequestedEventArgs	
Provides information about a FileUpdateRequested event.

StorageProviderFileTypeInfo	
 Note

Scenarios for this API are not supported.

StorageProviderGetContentInfoForPathResult	
Provides methods to get additional information about a content URI path.

StorageProviderGetPathForContentUriResult	
Provides methods to get additional information about a content URI result.

StorageProviderItemProperties	
Provides access to the properties of a Cloud Storage Provider storage item (like a file or folder).

StorageProviderItemProperty	
Defines a Cloud Storage Provider property for a storage item (like a file or folder).

StorageProviderItemPropertyDefinition	
Defines the properties of an item from a Cloud Storage Provider.

StorageProviderKnownFolderEntry	
Represents a known folder that is registered with the provider.

StorageProviderKnownFolderSyncInfo	
The StorageProviderKnownFolderSyncInfo class encapsulates all the information relevant to the provider’s backup/sync capability and status.

StorageProviderKnownFolderSyncRequestArgs	
The arguments that are provided to a StorageProviderKnownFolderSyncRequestedHandler delegate when a sync operation is requested for a known folder.

StorageProviderMoreInfoUI	
The container for the more info UI section of the storage provider flyout. This is used to provide additional information and/or a recommended action a user can take in response to the current sync state.

StorageProviderQueryResultSet	
The class that the storage provider uses to return the set of query results.

StorageProviderQuotaUI	
The container for the quota UI section of the storage provider flyout. This specifies the total amount of storage in the cloud files account and how much is used.

StorageProviderSearchQueryOptions	
Provides options for a search query.

StorageProviderSearchResult	
The class that the storage provider will use to return an individual search result.

StorageProviderStatusUI	
The container used to populate the storage provider status UI flyout.

StorageProviderSuggestionResult	
The class that the cloud storage provider will use to return an individual suggestion result.

 Important

The Windows.Storage.Provider.StorageProviderSuggestionResult API is part of a Limited Access Feature (see LimitedAccessFeatures class). For more information or to request an unlock token, please use the LAF Access Token Request Form.

StorageProviderSuggestionsQueryOptions	
Provides options for a suggestions query.

 Important

The Windows.Storage.Provider.StorageProviderSuggestionsQueryOptions API is part of a Limited Access Feature (see LimitedAccessFeatures class). For more information or to request an unlock token, please use the LAF Access Token Request Form.

StorageProviderSyncRootInfo	
Contains the properties of a Cloud Storage Provider's sync root to be registered with the operating system.

StorageProviderSyncRootManager	
Provides the ability to register or unregister a Cloud Storage Provider sync root with the operating system.

Interfaces
Name	Description
IStorageProviderItemPropertySource	
Provides access to storage provider item properties from the sync root.

IStorageProviderKnownFolderSyncInfoSource	
The interface that a cloud files provider implements to provide information about the sync status of known folders.

IStorageProviderKnownFolderSyncInfoSourceFactory	
The interface that a cloud files provider implements to provide instances of IStorageProviderKnownFolderSyncInfoSource to File Explorer.

IStorageProviderPropertyCapabilities	
Provides access to the property capabilities supported by the sync root.

IStorageProviderQueryResult	
The base interface for query results.

IStorageProviderSearchHandler	
This interface is implemented by a cloud files provider to enable the system to search for files and folders in the provider's sync root.

 Note

Cloud files search is supported in Windows 11, version 24H2 and later on Copilot+ PCs or AI-enabled Cloud PCs.

IStorageProviderSearchHandlerFactory	
This interface is implemented by a cloud files provider such as OneDrive. It is used to create a search handler that can be used to search for files and folders in the cloud files provider.

IStorageProviderShareLinkSource
IStorageProviderStatusUISource	
The contract implemented by a cloud files provider, which is required to use the storage provider status UI flyout.

IStorageProviderStatusUISourceFactory	
Returns an instance of IStorageProviderStatusUISource.

IStorageProviderSuggestionsHandler	
This interface is implemented by a cloud files provider to enable the system to query for suggested files.

 Important

The Windows.Storage.Provider.IStorageProviderSuggestionsHandler API is part of a Limited Access Feature (see LimitedAccessFeatures class). For more information or to request an unlock token, please use the LAF Access Token Request Form.

IStorageProviderSuggestionsHandlerFactory	
This interface is implemented by a cloud files provider to create a suggestions handler that can be used to query for suggested files from the provider.

 Important

The Windows.Storage.Provider.IStorageProviderSuggestionsHandlerFactory API is part of a Limited Access Feature (see LimitedAccessFeatures class). For more information or to request an unlock token, please use the LAF Access Token Request Form.

IStorageProviderUICommand	
UI Commands implemented by a storage provider.

IStorageProviderUriSource	
An interface for getting a content URI path and information.

Enums
Name	Description
CachedFileOptions	
Describes when Windows will request an update to a file.

CachedFileTarget	
Indicates whether updates should be applied to the locally cached copy or the remote version of the file.

FileUpdateStatus	
Describes the status of a file update request.

ReadActivationMode	
Indicates when Windows will request a file update if another app retrieves the file from its MostRecentlyUsedList or FutureAccessList.

StorageProviderHardlinkPolicy	
Specifies whether hard links are permitted on a placeholder file or folder. By default, hard links are not allowed on a placeholder.

StorageProviderHydrationPolicy	
An enumeration of file hydration policy values for a placeholder file. The hydration policy allows a sync root to customize behavior for retrieving data for a placeholder file.

StorageProviderHydrationPolicyModifier	
Provides policy modifiers to be used with the primary StorageProviderHydrationPolicy.

StorageProviderInSyncPolicy	
Contains the file and directory attributes supported by the sync root.

StorageProviderKnownFolderSyncStatus	
An enumeration that describes the sync enrollment status of a known folder.

StorageProviderPopulationPolicy	
Allows a sync provider to control how a placeholder file or directory

StorageProviderProtectionMode	
Indicates the type of data contained in the sync root.

StorageProviderResultKind	
Defines the possible kinds of storage provider results.

StorageProviderResultUsageKind	
Defines the possible usage kinds for a storage provider result.

StorageProviderSearchMatchKind	
Defines the kind of match that was found in a search query.

StorageProviderSearchQueryStatus	
The status of a search query.

StorageProviderShareLinkState
StorageProviderState	
Enumeration of the status of a storage provider state.

StorageProviderUICommandState	
This enum provides information that dictates the visibility and opacity of StorageProviderUICommands.

StorageProviderUriSourceStatus	
Enumeration of the status of a storage provider URI.

UIStatus	
Indicates the status of the file picker UI.

WriteActivationMode	
Indicates whether other apps can write to the locally cached version of the file and when Windows will request an update if another app writes to that local file.

Delegates
Name	Description
StorageProviderKnownFolderSyncRequestedHandler	
A delegate that is invoked when a sync operation is requested for a known folder.