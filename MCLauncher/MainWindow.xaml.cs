using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MCLauncher {
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http; // Add this at the beginning of your file
    using System.IO.Compression;
    using System.Threading;
    using System.Windows.Data;
    using Windows.ApplicationModel;
    using Windows.Foundation;
    using Windows.Management.Core;
    using Windows.Management.Deployment;
    using Windows.System;
    using WPFDataTypes;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, ICommonVersionCommands {

        private static readonly string PREFS_PATH = @"preferences.json";
        private static readonly string IMPORTED_VERSIONS_PATH = @"imported_versions";
        private static readonly string VERSIONS_API = "https://mrarm.io/r/w10-vdb";
        // Add these fields to your MainWindow class
        private static readonly string CURRENT_VERSION = "1.0b";
        private static readonly string VERSION_CHECK_URL = "https://raw.githubusercontent.com/danbenba/MinusLauncher/project/version.txt";
        private static readonly string DOWNLOAD_URL = "https://github.com/danbenba/MinusLauncher/releases/tag/lastrelease";

        private VersionList _versions;
        public Preferences UserPrefs { get; }

        private HashSet<CollectionViewSource> _versionListViews = new HashSet<CollectionViewSource>();

        private readonly VersionDownloader _anonVersionDownloader = new VersionDownloader();
        private readonly VersionDownloader _userVersionDownloader = new VersionDownloader();
        private readonly Task _userVersionDownloaderLoginTask;
        private volatile int _userVersionDownloaderLoginTaskStarted;
        private volatile bool _hasLaunchTask = false;

        public MainWindow() {
            if (File.Exists(PREFS_PATH)) {
                UserPrefs = JsonConvert.DeserializeObject<Preferences>(File.ReadAllText(PREFS_PATH));
            } else {
                UserPrefs = new Preferences();
                RewritePrefs();
            }

            var versionsApi = UserPrefs.VersionsApi != "" ? UserPrefs.VersionsApi : VERSIONS_API;
            _versions = new VersionList("versions.json", IMPORTED_VERSIONS_PATH, versionsApi, this, VersionEntryPropertyChanged);
            InitializeComponent();
            ShowInstalledVersionsOnlyCheckbox.DataContext = this;

            var versionListViewRelease = Resources["versionListViewRelease"] as CollectionViewSource;
            versionListViewRelease.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Release && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));
            });
            versionListViewRelease.Source = _versions;
            ReleaseVersionList.DataContext = versionListViewRelease;
            _versionListViews.Add(versionListViewRelease);

            var versionListViewBeta = Resources["versionListViewBeta"] as CollectionViewSource;
            versionListViewBeta.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Beta && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));
            });
            versionListViewBeta.Source = _versions;
            BetaVersionList.DataContext = versionListViewBeta;
            _versionListViews.Add(versionListViewBeta);

            var versionListViewPreview = Resources["versionListViewPreview"] as CollectionViewSource;
            versionListViewPreview.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Preview && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));
            });
            versionListViewPreview.Source = _versions;
            PreviewVersionList.DataContext = versionListViewPreview;
            _versionListViews.Add(versionListViewPreview);

            var versionListViewImported = Resources["versionListViewImported"] as CollectionViewSource;
            versionListViewImported.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Imported;
            });

            versionListViewImported.Source = _versions;
            ImportedVersionList.DataContext = versionListViewImported;
            _versionListViews.Add(versionListViewImported);

            _userVersionDownloaderLoginTask = new Task(() => {
                _userVersionDownloader.EnableUserAuthorization();
            });
            Dispatcher.Invoke(LoadVersionList);
            CheckForUpdatesAsync();
        }

        private async void CheckForUpdatesAsync()
        {
            using (var client = new HttpClient())
            {
                try
                {
                    string latestVersion = await client.GetStringAsync(VERSION_CHECK_URL);
                    latestVersion = latestVersion.Trim();

                    if (latestVersion != CURRENT_VERSION)
                    {
                        MessageBoxResult result = MessageBox.Show("Une mise à jour est disponible, voulez-vous la télécharger?", "Mise à jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = DOWNLOAD_URL,
                                UseShellExecute = true
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed to check for updates: " + ex.ToString());
                    // Handle exceptions such as no internet connection here
                }
            }
        }
        private async void LoadVersionList()
        {
            LoadingProgressLabel.Content = "Chargement des versions depuis le cache";
            LoadingProgressBar.Value = 1;

            LoadingProgressGrid.Visibility = Visibility.Visible;

            try
            {
                await _versions.LoadFromCache();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Échec du chargement du cache de la liste :\n" + e.ToString());
            }

            LoadingProgressLabel.Content = "Mise à jour de la liste des versions depuis " + _versions.VersionsApi;
            LoadingProgressBar.Value = 2;
            try
            {
                await _versions.DownloadList();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Échec du téléchargement de la liste :\n" + e.ToString());
                MessageBox.Show("Échec de la mise à jour de la liste des versions depuis internet. Certaines nouvelles versions peuvent manquer.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadingProgressLabel.Content = "Chargement des versions importées";
            LoadingProgressBar.Value = 3;
            await _versions.LoadImported();

            LoadingProgressGrid.Visibility = Visibility.Collapsed;
        }

        private void VersionEntryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RefreshLists();
        }

        private async void ImportButtonClicked(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDlg = new Microsoft.Win32.OpenFileDialog();
            openFileDlg.Filter = "Paquet d'application UWP (*.appx)|*.appx|Tous les fichiers|*.*";
            Nullable<bool> result = openFileDlg.ShowDialog();
            if (result == true)
            {
                string directory = Path.Combine(IMPORTED_VERSIONS_PATH, openFileDlg.SafeFileName);
                if (Directory.Exists(directory))
                {
                    var found = false;
                    foreach (var version in _versions)
                    {
                        if (version.IsImported && version.GameDirectory == directory)
                        {
                            if (version.IsStateChanging)
                            {
                                MessageBox.Show("Une version avec le même nom a déjà été importée et est actuellement en cours de modification. Veuillez patienter quelques instants et réessayer.", "Erreur");
                                return;
                            }
                            MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show("Une version avec le même nom a déjà été importée. Voulez-vous la supprimer ?", "Confirmation de suppression", System.Windows.MessageBoxButton.YesNo);
                            if (messageBoxResult == MessageBoxResult.Yes)
                            {
                                await Remove(version);
                                found = true;
                                break;
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                    if (!found)
                    {
                        MessageBox.Show("Le chemin de destination pour l'importation existe déjà et ne contient pas une installation de Minecraft connue par le lanceur. Pour éviter la perte de données, l'importation a été annulée. Veuillez supprimer les fichiers manuellement.", "Erreur");
                        return;
                    }
                }

                var versionEntry = _versions.AddEntry(openFileDlg.SafeFileName, directory);
                versionEntry.StateChangeInfo = new VersionStateChangeInfo(VersionState.Extracting);
                await Task.Run(() => {
                    try
                    {
                        ZipFile.ExtractToDirectory(openFileDlg.FileName, directory);
                    }
                    catch (InvalidDataException ex)
                    {
                        Debug.WriteLine("Échec de l'extraction de l'appx " + openFileDlg.FileName + " : " + ex.ToString());
                        MessageBox.Show("Échec de l'importation de l'appx " + openFileDlg.SafeFileName + ". Il peut être corrompu ou ne pas être un fichier appx.\n\nErreur d'extraction : " + ex.Message, "Échec de l'importation");
                        return;
                    }
                    finally
                    {
                        versionEntry.StateChangeInfo = null;
                    }
                });
            }
        }


        public ICommand LaunchCommand => new RelayCommand((v) => InvokeLaunch((Version)v));

        public ICommand RemoveCommand => new RelayCommand((v) => InvokeRemove((Version)v));

        public ICommand DownloadCommand => new RelayCommand((v) => InvokeDownload((Version)v));

        private void InvokeLaunch(Version v)
        {
            if (_hasLaunchTask)
                return;
            _hasLaunchTask = true;
            Task.Run(async () => {
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Registering);
                string gameDir = Path.GetFullPath(v.GameDirectory);
                try
                {
                    await ReRegisterPackage(v.GamePackageFamily, gameDir);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Échec de réenregistrement de l'application :\n" + e.ToString());
                    MessageBox.Show("Échec de réenregistrement de l'application :\n" + e.ToString());
                    _hasLaunchTask = false;
                    v.StateChangeInfo = null;
                    return;
                }
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Launching);
                try
                {
                    var pkg = await AppDiagnosticInfo.RequestInfoForPackageAsync(v.GamePackageFamily);
                    if (pkg.Count > 0)
                        await pkg[0].LaunchAsync();
                    Debug.WriteLine("Lancement de l'application terminé !");
                    _hasLaunchTask = false;
                    v.StateChangeInfo = null;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Échec du lancement de l'application :\n" + e.ToString());
                    MessageBox.Show("Échec du lancement de l'application :\n" + e.ToString());
                    _hasLaunchTask = false;
                    v.StateChangeInfo = null;
                    return;
                }
            });
        }

        private async Task DeploymentProgressWrapper(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> t)
        {
            TaskCompletionSource<int> src = new TaskCompletionSource<int>();
            t.Progress += (v, p) => {
                Debug.WriteLine("Progression du déploiement : " + p.state + " " + p.percentage + "%");
            };
            t.Completed += (v, p) => {
                if (p == AsyncStatus.Error)
                {
                    Debug.WriteLine("Échec du déploiement : " + v.GetResults().ErrorText);
                    src.SetException(new Exception("Échec du déploiement : " + v.GetResults().ErrorText));
                }
                else
                {
                    Debug.WriteLine("Déploiement terminé : " + p);
                    src.SetResult(1);
                }
            };
            await src.Task;
        }

        private string GetBackupMinecraftDataDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tmpDir = Path.Combine(localAppData, "TmpMinecraftLocalState");
            return tmpDir;
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            string title = "MinusLauncher";
            string creator = "danbenba";
            string version = "1.0 Beta"; // Fetch dynamically if possible
            string description = "\rCette application sert de lanceur pour Minecraft Bedrock,\npermettant de démarrer toutes les versions du jeu sans nécessiter un compte Microsoft.\r\n\r";

            string aboutText = $"{title}\n\n\n{description}\n\rVersion: {version} (Portable Version)\nPowered by {creator}\n\n\r\n© danbenba 2024";
            AboutTextBlock.Text = aboutText;
        }

        private void BackupMinecraftDataForRemoval(string packageFamily)
        {
            var data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            string tmpDir = GetBackupMinecraftDataDir();
            if (Directory.Exists(tmpDir))
            {
                Debug.WriteLine("Erreur BackupMinecraftDataForRemoval : " + tmpDir + " existe déjà");
                Process.Start("explorer.exe", tmpDir);
                MessageBox.Show("Le répertoire temporaire pour la sauvegarde des données de MC existe déjà. Cela signifie probablement que nous avons échoué lors de la dernière sauvegarde des données. Veuillez sauvegarder le répertoire manuellement.");
                throw new Exception("Le répertoire temporaire existe");
            }
            Debug.WriteLine("Déplacement des données de Minecraft vers : " + tmpDir);
            Directory.Move(data.LocalFolder.Path, tmpDir);
        }

        private void RestoreMove(string from, string to)
        {
            foreach (var f in Directory.EnumerateFiles(from))
            {
                string ft = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(ft))
                {
                    if (MessageBox.Show("Le fichier " + ft + " existe déjà dans la destination.\nVoulez-vous le remplacer ? L'ancien fichier sera perdu.", "Restauration du répertoire de données de l'installation précédente", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    File.Delete(ft);
                }
                File.Move(f, ft);
            }
            foreach (var f in Directory.EnumerateDirectories(from))
            {
                string tp = Path.Combine(to, Path.GetFileName(f));
                if (!Directory.Exists(tp))
                {
                    if (File.Exists(tp) && MessageBox.Show("Le fichier " + tp + " n'est pas un répertoire. Voulez-vous le supprimer ? Les données de l'ancien répertoire seront perdues.", "Restauration du répertoire de données de l'installation précédente", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    Directory.CreateDirectory(tp);
                }
                RestoreMove(f, tp);
            }
        }


        private void RestoreMinecraftDataFromReinstall(string packageFamily)
        {
            string tmpDir = GetBackupMinecraftDataDir();
            if (!Directory.Exists(tmpDir))
                return;
            var data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            Debug.WriteLine("Déplacement des données de sauvegarde de Minecraft vers : " + data.LocalFolder.Path);
            RestoreMove(tmpDir, data.LocalFolder.Path);
            Directory.Delete(tmpDir, true);
        }

        private async Task RemovePackage(Package pkg, string packageFamily)
        {
            Debug.WriteLine("Suppression du package : " + pkg.Id.FullName);
            if (!pkg.IsDevelopmentMode)
            {
                BackupMinecraftDataForRemoval(packageFamily);
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, 0));
            }
            else
            {
                Debug.WriteLine("Le package est en mode développement");
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.PreserveApplicationData));
            }
            Debug.WriteLine("Suppression du package terminée : " + pkg.Id.FullName);
        }

        private string GetPackagePath(Package pkg)
        {
            try
            {
                return pkg.InstalledLocation.Path;
            }
            catch (FileNotFoundException)
            {
                return "";
            }
        }

        private async Task UnregisterPackage(string packageFamily, string gameDir)
        {
            foreach (var pkg in new PackageManager().FindPackages(packageFamily))
            {
                string location = GetPackagePath(pkg);
                if (location == "" || location == gameDir)
                {
                    await RemovePackage(pkg, packageFamily);
                }
            }
        }

        private async Task ReRegisterPackage(string packageFamily, string gameDir)
        {
            foreach (var pkg in new PackageManager().FindPackages(packageFamily))
            {
                string location = GetPackagePath(pkg);
                if (location == gameDir)
                {
                    Debug.WriteLine("Annulation de la suppression du package - même chemin : " + pkg.Id.FullName + " " + location);
                    return;
                }
                await RemovePackage(pkg, packageFamily);
            }
            Debug.WriteLine("Enregistrement du package");
            string manifestPath = Path.Combine(gameDir, "AppxManifest.xml");
            await DeploymentProgressWrapper(new PackageManager().RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.DevelopmentMode));
            Debug.WriteLine("Réenregistrement de l'app terminé !");
            RestoreMinecraftDataFromReinstall(packageFamily);
        }


        private void InvokeDownload(Version v)
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.IsNew = false;
            v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Initializing);
            v.StateChangeInfo.CancelCommand = new RelayCommand((o) => cancelSource.Cancel());

            Debug.WriteLine("Début du téléchargement");
            Task.Run(async () => {
                string dlPath = (v.VersionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + v.Name + ".Appx";
                VersionDownloader downloader = _anonVersionDownloader;
                if (v.VersionType == VersionType.Beta)
                {
                    downloader = _userVersionDownloader;
                    if (Interlocked.CompareExchange(ref _userVersionDownloaderLoginTaskStarted, 1, 0) == 0)
                    {
                        _userVersionDownloaderLoginTask.Start();
                    }
                    Debug.WriteLine("En attente d'authentification");
                    try
                    {
                        await _userVersionDownloaderLoginTask;
                        Debug.WriteLine("Authentification réussie");
                    }
                    catch (WUTokenHelper.WUTokenException e)
                    {
                        Debug.WriteLine("Échec de l'authentification:\n" + e.ToString());
                        MessageBox.Show("Échec de l'authentification car : " + e.Message, "Échec de l'authentification");
                        v.StateChangeInfo = null;
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Échec de l'authentification:\n" + e.ToString());
                        MessageBox.Show(e.ToString(), "Échec de l'authentification");
                        v.StateChangeInfo = null;
                        return;
                    }
                }
                try
                {
                    await downloader.Download(v.UUID, "1", dlPath, (current, total) => {
                        if (v.StateChangeInfo.VersionState != VersionState.Downloading)
                        {
                            Debug.WriteLine("Début du téléchargement réel");
                            v.StateChangeInfo.VersionState = VersionState.Downloading;
                            if (total.HasValue)
                                v.StateChangeInfo.TotalSize = total.Value;
                        }
                        v.StateChangeInfo.DownloadedBytes = current;
                    }, cancelSource.Token);
                    Debug.WriteLine("Téléchargement terminé");
                }
                catch (BadUpdateIdentityException)
                {
                    Debug.WriteLine("Échec du téléchargement dû à l'échec de récupération de l'URL de téléchargement");
                    MessageBox.Show(
                        "Impossible de récupérer l'URL de téléchargement pour la version." +
                        (v.VersionType == VersionType.Beta ? "\nPour les versions bêta, veuillez vous assurer que votre compte est abonné au programme bêta de Minecraft dans l'application Xbox Insider Hub." : "")
                    );
                    v.StateChangeInfo = null;
                    return;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Échec du téléchargement:\n" + e.ToString());
                    if (!(e is TaskCanceledException))
                        MessageBox.Show("Échec du téléchargement:\n" + e.ToString());
                    v.StateChangeInfo = null;
                    return;
                }
                try
                {
                    v.StateChangeInfo.VersionState = VersionState.Extracting;
                    string dirPath = v.GameDirectory;
                    if (Directory.Exists(dirPath))
                        Directory.Delete(dirPath, true);
                    ZipFile.ExtractToDirectory(dlPath, dirPath);
                    v.StateChangeInfo = null;
                    File.Delete(Path.Combine(dirPath, "AppxSignature.p7x"));
                    if (UserPrefs.DeleteAppxAfterDownload)
                    {
                        Debug.WriteLine("Suppression de l'APPX pour réduire l'utilisation du disque");
                        File.Delete(dlPath);
                    }
                    else
                    {
                        Debug.WriteLine("APPX non supprimé en raison des préférences de l'utilisateur");
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Échec de l'extraction:\n" + e.ToString());
                    MessageBox.Show("Échec de l'extraction:\n" + e.ToString());
                    v.StateChangeInfo = null;
                    return;
                }
                v.StateChangeInfo = null;
                v.UpdateInstallStatus();
            });
        }


        private async Task Remove(Version v) {
            v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Uninstalling);
            await UnregisterPackage(v.GamePackageFamily, Path.GetFullPath(v.GameDirectory));
            Directory.Delete(v.GameDirectory, true);
            v.StateChangeInfo = null;
            if (v.IsImported) {
                Dispatcher.Invoke(() => _versions.Remove(v));
                Debug.WriteLine("Version importée supprimée " + v.DisplayName);
            }
            else {
                v.UpdateInstallStatus();
                Debug.WriteLine("Version supprimée " + v.DisplayName);
            }
        }

        private void InvokeRemove(Version v) {
            Task.Run(async () => await Remove(v));
        }

        private void ShowInstalledVersionsOnlyCheckbox_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.ShowInstalledOnly = ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false;
            RefreshLists();
            RewritePrefs();
        }

        private void RefreshLists() {
            Dispatcher.Invoke(() => {
                foreach (var list in _versionListViews) {
                    list.View.Refresh();
                }
            });
        }

        private void DeleteAppxAfterDownloadCheck_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.DeleteAppxAfterDownload = DeleteAppxAfterDownloadOption.IsChecked;
        }

        private void RewritePrefs() {
            File.WriteAllText(PREFS_PATH, JsonConvert.SerializeObject(UserPrefs));
        }

        private void MenuItemOpenLogFileClicked(object sender, RoutedEventArgs e) {
            if (!File.Exists(@"Log.txt")) {
                MessageBox.Show("Log file not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } else 
                Process.Start(@"Log.txt");
        }

        private void MenuItemOpenDataDirClicked(object sender, RoutedEventArgs e) {
            Process.Start(@"explorer.exe", Directory.GetCurrentDirectory());
        }

        private void MenuItemCleanupForMicrosoftStoreReinstallClicked(object sender, RoutedEventArgs e) {
            var result = MessageBox.Show(
                "Les versions de Minecraft installées par le lanceur seront désinstallées.\n" +
     "Cela vous permettra de réinstaller Minecraft depuis le Microsoft Store. Vos données (mondes, etc.) ne seront pas supprimées.\n\n" +
     "Es-tu sur de vouloir continuer?",
"Désinstaller",
                MessageBoxButton.OKCancel
            );
            if (result == MessageBoxResult.OK) {
                Debug.WriteLine("Démarrage de la désinstallation !\r\n​");
                foreach (var version in _versions) {
                    if (version.IsInstalled) {
                        InvokeRemove(version);
                    }
                }
                Debug.WriteLine("Désinstallation programmée");
            }
        }

        private void MenuItemRefreshVersionListClicked(object sender, RoutedEventArgs e) {
            Dispatcher.Invoke(LoadVersionList);
        }

        private void onEndpointChangedHandler(object sender, string newEndpoint) {
            UserPrefs.VersionsApi = newEndpoint;
            _versions.VersionsApi = newEndpoint == "" ? VERSIONS_API : newEndpoint;
            Dispatcher.Invoke(LoadVersionList);
            RewritePrefs();
        }

        private void MenuItemSetVersionListEndpointClicked(object sender, RoutedEventArgs e) {
            var dialog = new VersionListEndpointDialog(UserPrefs.VersionsApi) {
                Owner = this
            };
            dialog.OnEndpointChanged += onEndpointChangedHandler;

            dialog.Show();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }

    struct MinecraftPackageFamilies
    {
        public static readonly string MINECRAFT = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        public static readonly string MINECRAFT_PREVIEW = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";
    }

    namespace WPFDataTypes {


        public class NotifyPropertyChangedBase : INotifyPropertyChanged {

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged(string name) {
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(name));
            }

        }

        public interface ICommonVersionCommands {

            ICommand LaunchCommand { get; }

            ICommand DownloadCommand { get; }

            ICommand RemoveCommand { get; }

        }

        public enum VersionType : int
        {
            Release = 0,
            Beta = 1,
            Preview = 2,
            Imported = 100
        }

        public class Version : NotifyPropertyChangedBase {
            public static readonly string UNKNOWN_UUID = "UNKNOWN";

            public Version(string uuid, string name, VersionType versionType, bool isNew, ICommonVersionCommands commands) {
                this.UUID = uuid;
                this.Name = name;
                this.VersionType = versionType;
                this.IsNew = isNew;
                this.DownloadCommand = commands.DownloadCommand;
                this.LaunchCommand = commands.LaunchCommand;
                this.RemoveCommand = commands.RemoveCommand;
                this.GameDirectory = (versionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + Name;
            }
            public Version(string name, string directory, ICommonVersionCommands commands) {
                this.UUID = UNKNOWN_UUID;
                this.Name = name;
                this.VersionType = VersionType.Imported;
                this.DownloadCommand = commands.DownloadCommand;
                this.LaunchCommand = commands.LaunchCommand;
                this.RemoveCommand = commands.RemoveCommand;
                this.GameDirectory = directory;
            }

            public string UUID { get; set; }
            public string Name { get; set; }
            public VersionType VersionType { get; set; }
            public bool IsNew {
                get { return _isNew; }
                set {
                    _isNew = value;
                    OnPropertyChanged("IsNew");
                }
            }
            public bool IsImported {
                get => VersionType == VersionType.Imported;
            }

            public string GameDirectory { get; set; }

            public string GamePackageFamily
            {
                get => VersionType == VersionType.Preview ? MinecraftPackageFamilies.MINECRAFT_PREVIEW : MinecraftPackageFamilies.MINECRAFT;
            }

            public bool IsInstalled => Directory.Exists(GameDirectory);

            public string DisplayName {
                get {
                    string typeTag = "";
                    if (VersionType == VersionType.Beta)
                        typeTag = "(beta)";
                    else if (VersionType == VersionType.Preview)
                        typeTag = "(preview)";
                    return Name + (typeTag.Length > 0 ? " " + typeTag : "") + (IsNew ? " (NEW!)" : "");
                }
            }
            public string DisplayInstallStatus {
                get {
                    return IsInstalled ? "Installé" : "Non installé";
                }
            }

            public ICommand LaunchCommand { get; set; }
            public ICommand DownloadCommand { get; set; }
            public ICommand RemoveCommand { get; set; }

            private VersionStateChangeInfo _stateChangeInfo;
            private bool _isNew = false;
            public VersionStateChangeInfo StateChangeInfo {
                get { return _stateChangeInfo; }
                set { _stateChangeInfo = value; OnPropertyChanged("StateChangeInfo"); OnPropertyChanged("IsStateChanging"); }
            }

            public bool IsStateChanging => StateChangeInfo != null;

            public void UpdateInstallStatus() {
                OnPropertyChanged("IsInstalled");
            }

        }

        public enum VersionState {
            Initializing,
            Downloading,
            Extracting,
            Registering,
            Launching,
            Uninstalling
        };

        public class VersionStateChangeInfo : NotifyPropertyChangedBase {

            private VersionState _versionState;

            private long _downloadedBytes;
            private long _totalSize;

            public VersionStateChangeInfo(VersionState versionState) {
                _versionState = versionState;
            }

            public VersionState VersionState {
                get { return _versionState; }
                set {
                    _versionState = value;
                    OnPropertyChanged("IsProgressIndeterminate");
                    OnPropertyChanged("DisplayStatus");
                }
            }

            public bool IsProgressIndeterminate {
                get {
                    switch (_versionState) {
                        case VersionState.Initializing:
                        case VersionState.Extracting:
                        case VersionState.Uninstalling:
                        case VersionState.Registering:
                        case VersionState.Launching:
                            return true;
                        default: return false;
                    }
                }
            }

            public long DownloadedBytes {
                get { return _downloadedBytes; }
                set { _downloadedBytes = value; OnPropertyChanged("DownloadedBytes"); OnPropertyChanged("DisplayStatus"); }
            }

            public long TotalSize {
                get { return _totalSize; }
                set { _totalSize = value; OnPropertyChanged("TotalSize"); OnPropertyChanged("DisplayStatus"); }
            }

            public string DisplayStatus {
                get {
                    switch (_versionState) {
                        case VersionState.Initializing: return "Preparing...";
                        case VersionState.Downloading:
                            return "Downloading... " + (DownloadedBytes / 1024 / 1024) + "MiB/" + (TotalSize / 1024 / 1024) + "MiB";
                        case VersionState.Extracting: return "Extracting...";
                        case VersionState.Registering: return "Registering package...";
                        case VersionState.Launching: return "Launching...";
                        case VersionState.Uninstalling: return "Uninstalling...";
                        default: return "Que se passe-t-il ? ...";
                    }
                }
            }

            public ICommand CancelCommand { get; set; }

        }

    }
}
