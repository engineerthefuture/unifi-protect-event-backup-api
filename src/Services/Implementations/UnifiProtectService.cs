/************************
 * Unifi Webhook Event Receiver
 * UnifiProtectService.cs
 * 
 * Service for Unifi Protect system integration.
 * Handles video downloads and file cleanup operations using headless browser automation.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Diagnostics.CodeAnalysis;
using Amazon.Lambda.Core;
using PuppeteerSharp;
using HeadlessChromium.Puppeteer.Lambda.Dotnet;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;
using System.Text.Json;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for Unifi Protect system integration.
    /// </summary>
    public class UnifiProtectService : IUnifiProtectService
    {
        private readonly ILambdaLogger _logger;
        private readonly IS3StorageService _s3StorageService;
        private readonly ICredentialsService _credentialsService;

        /// <summary>
        /// Maximum time in seconds to wait for video download to complete
        /// </summary>
        private const int MaxVideoDownloadWaitTimeSeconds = 118;

        /// <summary>
        /// Initializes a new instance of the UnifiProtectService.
        /// </summary>
        /// <param name="logger">Lambda logger instance</param>
        /// <param name="s3StorageService">S3 storage service for screenshot uploads</param>
        /// <param name="credentialsService">Credentials service for Unifi Protect authentication</param>
        public UnifiProtectService(ILambdaLogger logger, IS3StorageService s3StorageService, ICredentialsService credentialsService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _s3StorageService = s3StorageService ?? throw new ArgumentNullException(nameof(s3StorageService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
        }

        /// <summary>
        /// Downloads a video file from Unifi Protect for a specific event.
        /// </summary>
        /// <param name="trigger">The trigger containing event information</param>
        /// <param name="eventLocalLink">Direct URL to the event in Unifi Protect</param>
        /// <param name="timestamp">The event timestamp for consistent S3 key generation</param>
        /// <returns>Path to the downloaded video file</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser and external network connectivity
        public async Task<string> DownloadVideoAsync(Trigger trigger, string eventLocalLink, long timestamp)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventLocalLink);
            if (timestamp <= 0)
                throw new ArgumentException("Timestamp must be greater than zero", nameof(timestamp));

            _logger.LogLine($"Starting video download for event from URL: {eventLocalLink}");
            _logger.LogLine($"Trigger details - EventId: {trigger.eventId}, Device: {trigger.device}, DeviceName: {trigger.deviceName}");

            // The downloaded video will be stored temporarily and then moved to S3
            var downloadDirectory = AppConfiguration.DownloadDirectory;
            _logger.LogLine($"Using download directory: {downloadDirectory}");
            
            var tempVideoFile = Path.Combine(downloadDirectory, $"temp_{trigger.eventId}_{DateTime.UtcNow.Ticks}.mp4");
            _logger.LogLine($"Temporary video file path: {tempVideoFile}");

            try
            {
                // Ensure download directory exists
                if (!Directory.Exists(downloadDirectory))
                {
                    Directory.CreateDirectory(downloadDirectory);
                    _logger.LogLine($"Created download directory: {downloadDirectory}");
                }

                // Verify credentials are available before attempting browser operations
                _logger.LogLine("Validating Unifi credentials...");
                var credentials = await _credentialsService.GetUnifiCredentialsAsync();
                if (credentials == null || string.IsNullOrEmpty(credentials.username) || string.IsNullOrEmpty(credentials.password))
                {
                    throw new InvalidOperationException("Unifi credentials are not properly configured");
                }
                _logger.LogLine("Credentials validated successfully");

                // This would call the headless browser logic (simplified for decomposition)
                // In the actual implementation, this would contain all the browser automation code
                _logger.LogLine("About to call DownloadVideoFromUnifiProtect");
                var videoData = await DownloadVideoFromUnifiProtect(eventLocalLink, trigger.deviceName ?? "", downloadDirectory, trigger, timestamp);
                _logger.LogLine($"DownloadVideoFromUnifiProtect completed, received {videoData.Length} bytes");
                
                // Save to temporary file
                await File.WriteAllBytesAsync(tempVideoFile, videoData);
                _logger.LogLine($"Video saved to temporary file: {tempVideoFile}");

                return tempVideoFile;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error downloading video: {ex.Message}");
                _logger.LogLine($"Exception type: {ex.GetType().Name}");
                _logger.LogLine($"Stack trace: {ex.StackTrace}");
                // Clean up any partial file
                CleanupTempFile(tempVideoFile);
                throw;
            }
        }

        /// <summary>
        /// Cleans up temporary video files.
        /// </summary>
        /// <param name="filePath">Path to the file to clean up</param>
        public void CleanupTempFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogLine($"Cleaned up temporary file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Warning: Could not clean up temporary file {filePath}: {ex.Message}");
            }
        }

        #region Private Video Download Implementation

        /// <summary>
        /// Downloads video from Unifi Protect using automated browser navigation.
        /// 
        /// This method uses HeadlessChromium to automate a headless browser session that:
        /// 1. Navigates to the Unifi Protect event link
        /// 2. Authenticates using stored credentials
        /// 3. Downloads the video file for the event using device-specific coordinates
        /// 4. Returns the video data as a byte array
        /// </summary>
        /// <param name="eventLocalLink">Direct URL to the event in Unifi Protect web interface</param>
        /// <param name="deviceName">Name of the device to determine appropriate click coordinates</param>
        /// <param name="downloadDirectory">Directory to use for downloads</param>
        /// <param name="trigger">The trigger information for screenshot naming</param>
        /// <param name="timestamp">The event timestamp for screenshot naming</param>
        /// <returns>Byte array containing the downloaded video data</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser and external network connectivity
        private async Task<byte[]> DownloadVideoFromUnifiProtect(string eventLocalLink, string deviceName, string downloadDirectory, Trigger trigger, long timestamp)
        {
            try
            {
                // Launch headless browser with optimized settings
                using var browser = await LaunchOptimizedBrowser();
                using var page = await SetupBrowserPage(browser);

                // Configure download behavior and directory
                await ConfigureDownloadBehavior(page, downloadDirectory);

                // Navigate to the event link and handle authentication
                await NavigateAndAuthenticate(page, eventLocalLink, downloadDirectory, trigger, timestamp);

                // Wait for page to be ready for interaction and get the event handler for cleanup
                var downloadEventHandler = await WaitForPageReady(page, downloadDirectory, trigger, timestamp);

                try
                {
                    // Perform video download actions
                    await PerformVideoDownloadActions(page, deviceName, downloadDirectory, trigger, timestamp);

                    // Wait for download to complete and get video data
                    var videoData = await WaitForDownloadAndGetVideoData(downloadDirectory);

                    // Perform sign out and capture screenshot (screenshot will be saved to S3)
                    await PerformSignOutAndCapture(page, downloadDirectory, trigger, timestamp);

                    return videoData;
                }
                finally
                {
                    // Clean up the event handler FIRST to prevent disposed object access
                    await CleanupEventHandler(page, downloadEventHandler);
                }
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogLine($"Browser was disposed during operation: {ex.Message}");
                throw new InvalidOperationException("Video download failed due to browser lifecycle issue. Please try again.");
            }
            catch (Exception ex)
            {
                // Filter out disposed object errors and provide cleaner error messages
                if (IsDisposedObjectError(ex))
                {
                    _logger.LogLine($"Disposed object error detected: {ex.Message}");
                    throw new InvalidOperationException("Video download failed due to browser cleanup issue. Please try again.");
                }
                
                _logger.LogLine($"Error while processing video download: {ex.Message}");
                throw new InvalidOperationException($"Error downloading video: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Calculates device-specific click coordinates for video download automation.
        /// </summary>
        /// <param name="deviceName">Name of the device to determine coordinates for</param>
        /// <returns>Tuple containing archive and download button coordinates</returns>
        private static ((int x, int y) archiveButton, (int x, int y) downloadButton) GetDeviceSpecificCoordinates(string deviceName)
        {
            // For "Door" device, use the default coordinates from environment variables
            if (string.Equals(deviceName, "Door", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    archiveButton: (AppConfiguration.ArchiveButtonX, AppConfiguration.ArchiveButtonY),
                    downloadButton: (AppConfiguration.DownloadButtonX, AppConfiguration.DownloadButtonY)
                );
            }
            
            // For all other devices, use adjusted coordinates
            int archiveX = 1205;
            int archiveY = 241;
            int downloadX = archiveX - 179;  // 1205 - 179 = 1026
            int downloadY = archiveY + 18;   // 241 + 18 = 259
            
            return (
                archiveButton: (archiveX, archiveY),
                downloadButton: (downloadX, downloadY)
            );
        }

        /// <summary>
        /// Launches an optimized headless browser for video downloading.
        /// </summary>
        /// <returns>Browser instance configured for video downloading</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser infrastructure
        private async Task<IBrowser> LaunchOptimizedBrowser()
        {
            _logger.LogLine("Launching headless browser with HeadlessChromium...");

            try
            {
                var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
                var browserLauncher = new HeadlessChromiumPuppeteerLauncher(loggerFactory);

                var chromeArgs = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--disable-web-security",
                    "--ignore-certificate-errors",
                    "--ignore-ssl-errors",
                    "--ignore-certificate-errors-spki-list",
                    "--no-first-run",
                    "--no-zygote",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-renderer-backgrounding",
                    "--disable-features=VizDisplayCompositor",
                    "--window-size=1920,1080",
                    "--disable-extensions",
                    "--disable-plugins",
                    "--disable-default-apps",
                    "--allow-running-insecure-content",
                    "--disable-background-networking",
                    "--enable-logging",
                    "--disable-ipc-flooding-protection"
                };

                var browser = await browserLauncher.LaunchAsync(chromeArgs);
                return browser;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Failed to launch browser: {ex.Message}");
                throw new InvalidOperationException($"Browser launch failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sets up a browser page with proper viewport and configuration.
        /// </summary>
        /// <param name="browser">The browser instance</param>
        /// <returns>Configured page instance</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser infrastructure
        private static async Task<IPage> SetupBrowserPage(IBrowser browser)
        {
            var page = await browser.NewPageAsync();

            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 1920,
                Height = 1080
            });

            return page;
        }

        /// <summary>
        /// Configures download behavior and ensures download directory exists.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="downloadDirectory">The download directory path</param>
        [ExcludeFromCodeCoverage] // Requires headless browser infrastructure
        private async Task ConfigureDownloadBehavior(IPage page, string downloadDirectory)
        {
            // Ensure the download directory exists
            if (!Directory.Exists(downloadDirectory))
            {
                Directory.CreateDirectory(downloadDirectory);
                _logger.LogLine($"Created download directory: {downloadDirectory}");
            }

            _logger.LogLine($"Using download directory: {downloadDirectory}");

            // Configure download behavior using CDP (Chrome DevTools Protocol)
            await page.Client.SendAsync("Page.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = downloadDirectory,
                eventsEnabled = true
            });

            try
            {
                await page.Client.SendAsync("Browser.setDownloadBehavior", new
                {
                    behavior = "allow",
                    downloadPath = downloadDirectory,
                    eventsEnabled = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Could not set Browser.setDownloadBehavior (this is normal): {ex.Message}");
            }
        }

        /// <summary>
        /// Navigates to the event URL and handles authentication if required.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="eventLocalLink">The event URL</param>
        /// <param name="downloadDirectory">Download directory for screenshots</param>
        /// <param name="trigger">The trigger information for screenshot naming</param>
        /// <param name="timestamp">The event timestamp for screenshot naming</param>
        private async Task NavigateAndAuthenticate(IPage page, string eventLocalLink, string downloadDirectory, Trigger trigger, long timestamp)
        {
            _logger.LogLine($"Navigating to Unifi Protect: {eventLocalLink}");

            // Navigate to the event link
            await page.GoToAsync(eventLocalLink, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                Timeout = 20000
            });
            _logger.LogLine("Page loaded, checking for login form...");

            // Check if we need to login
            var usernameField = await page.QuerySelectorAsync("input[name='username'], input[type='email'], input[id*='username'], input[id*='email']");
            var passwordField = await page.QuerySelectorAsync("input[name='password'], input[type='password'], input[id*='password']");

            // Take a screenshot of the page and upload to S3
            var screenshotPath = Path.Combine(downloadDirectory, "login-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            _logger.LogLine($"Screenshot taken: {screenshotPath}");
            
            // Upload screenshot to S3
            await UploadScreenshotToS3(screenshotPath, "login-screenshot.png", trigger, timestamp);
            
            // Clean up local screenshot file
            if (File.Exists(screenshotPath))
            {
                File.Delete(screenshotPath);
                _logger.LogLine($"Local screenshot file deleted: {screenshotPath}");
            }

            // Check if username and password fields are present
            if (usernameField != null && passwordField != null)
            {
                _logger.LogLine("Login form detected, retrieving credentials and performing authentication...");
                
                // Get Unifi credentials from AWS Secrets Manager
                var credentials = await _credentialsService.GetUnifiCredentialsAsync();
                
                if (string.IsNullOrEmpty(credentials.username) || string.IsNullOrEmpty(credentials.password))
                {
                    _logger.LogLine("ERROR: Unifi credentials are empty or missing");
                    throw new InvalidOperationException("Unifi credentials are not properly configured in AWS Secrets Manager");
                }
                
                _logger.LogLine($"Filling login form with username: {credentials.username}");
                
                // Fill in the username field
                await usernameField.ClickAsync();
                await usernameField.TypeAsync(credentials.username);
                _logger.LogLine("Username field filled");
                
                // Fill in the password field
                await passwordField.ClickAsync();
                await passwordField.TypeAsync(credentials.password);
                _logger.LogLine("Password field filled");
                
                // Look for login button and submit
                var loginButton = await page.QuerySelectorAsync("button[type='submit']");
                if (loginButton != null)
                {
                    _logger.LogLine("Clicking login button...");
                    await loginButton.ClickAsync();
                    
                    // Wait for navigation after login
                    try
                    {
                        await page.WaitForNavigationAsync(new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                            Timeout = 15000
                        });
                        _logger.LogLine("Login completed, page navigated");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogLine($"Navigation timeout after login (this may be normal): {ex.Message}");
                    }
                }
                else
                {
                    // If no login button found, try pressing Enter on password field
                    _logger.LogLine("No login button found, pressing Enter on password field");
                    await passwordField.PressAsync("Enter");
                    
                    // Wait a moment for the form submission
                    await Task.Delay(2000);
                }
                
                _logger.LogLine("Authentication process completed");
            }
            else
            {
                _logger.LogLine("No login form detected, proceeding without authentication");
            }
        }

        /// <summary>
        /// Waits for the page to be ready for interaction after authentication.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="downloadDirectory">Download directory for screenshots</param>
        /// <param name="trigger">The trigger information for screenshot naming</param>
        /// <param name="timestamp">The event timestamp for screenshot naming</param>
        /// <returns>The download event handler that was attached</returns>
        private async Task<EventHandler<MessageEventArgs>?> WaitForPageReady(IPage page, string downloadDirectory, Trigger trigger, long timestamp)
        {
            _logger.LogLine("Waiting for page to load after authentication...");

            try
            {
                await Task.Delay(2000);
                var isReady = await page.EvaluateExpressionAsync<bool>("document.readyState === 'complete'");
                if (!isReady)
                {
                    await page.WaitForFunctionAsync("() => document.readyState === 'complete'", new WaitForFunctionOptions
                    {
                        Timeout = 10000
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Timeout waiting for page ready state: {ex.Message}, but continuing");
            }

            bool downloadStarted = false;
            string? downloadGuid = null;

            EventHandler<MessageEventArgs> downloadEventHandler = (sender, e) =>
            {
                try
                {
                    ProcessDownloadEvent(e.MessageID, e.MessageData, ref downloadStarted, ref downloadGuid);
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"Error processing download event: {ex.Message}");
                }
            };

            page.Client.MessageReceived += downloadEventHandler;

            // Take a screenshot and upload to S3
            await Task.Delay(3000);
            var screenshotPath = Path.Combine(downloadDirectory, "pageload-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            _logger.LogLine($"Screenshot taken of the loaded page: {screenshotPath}");
            
            // Upload screenshot to S3
            await UploadScreenshotToS3(screenshotPath, "pageload-screenshot.png", trigger, timestamp);
            
            // Clean up local screenshot file
            if (File.Exists(screenshotPath))
            {
                File.Delete(screenshotPath);
                _logger.LogLine($"Local screenshot file deleted: {screenshotPath}");
            }

            return downloadEventHandler;
        }

        /// <summary>
        /// Performs the video download actions by clicking archive and download buttons.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="deviceName">Device name for coordinate calculation</param>
        /// <param name="downloadDirectory">Download directory for screenshots</param>
        /// <param name="trigger">The trigger information for screenshot naming</param>
        /// <param name="timestamp">The event timestamp for screenshot naming</param>
        private async Task PerformVideoDownloadActions(IPage page, string deviceName, string downloadDirectory, Trigger trigger, long timestamp)
        {
            var coordinates = GetDeviceSpecificCoordinates(deviceName);
            
            _logger.LogLine($"Device: {deviceName ?? "Unknown"} - Using archive button coordinates: ({coordinates.archiveButton.x}, {coordinates.archiveButton.y})");

            // Click archive button
            await page.Mouse.ClickAsync(coordinates.archiveButton.x, coordinates.archiveButton.y);
            _logger.LogLine("Clicked on archive button at coordinates: " + coordinates.archiveButton);

            var screenshotPath = Path.Combine(downloadDirectory, "afterarchivebuttonclick-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            _logger.LogLine($"Screenshot taken of the clicked archive button: {screenshotPath}");
            
            // Upload screenshot to S3
            await UploadScreenshotToS3(screenshotPath, "afterarchivebuttonclick-screenshot.png", trigger, timestamp);
            
            // Clean up local screenshot file
            if (File.Exists(screenshotPath))
            {
                File.Delete(screenshotPath);
                _logger.LogLine($"Local screenshot file deleted: {screenshotPath}");
            }
        }

        /// <summary>
        /// Performs sign out from Unifi Protect and captures a screenshot for failure email.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="downloadDirectory">Directory for storing the screenshot</param>
        /// <param name="trigger">The trigger information for screenshot naming</param>
        /// <param name="timestamp">The event timestamp</param>
        private async Task PerformSignOutAndCapture(IPage page, string downloadDirectory, Trigger trigger, long timestamp)
        {
            try
            {
                _logger.LogLine("Starting sign out process...");

                /*
                // Try to find and click the sign out button
                var signOutElement = await FindSignOutElement(page);

                if (signOutElement != null)
                {
                    await ClickSignOutButton(signOutElement);
                }
                else
                {
                    _logger.LogLine("Sign out button not found, proceeding without sign out");
                }
                */
                
                var screenshotPath = Path.Combine(downloadDirectory, "signout-screenshot.png");
                await page.ScreenshotAsync(screenshotPath);
                _logger.LogLine($"Screenshot taken of the signout: {screenshotPath}");
            
                // Upload screenshot to S3
                await UploadScreenshotToS3(screenshotPath, "signout-screenshot.png", trigger, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error during sign out process: {ex.Message}");
                // Don't throw - sign out failure shouldn't break the main process
            }
        }

        /// <summary>
        /// Finds the sign out element on the page, trying direct selectors first, then user menus.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <returns>The sign out element if found, null otherwise</returns>
        private async Task<IElementHandle?> FindSignOutElement(IPage page)
        {
            // Try direct sign out selectors first
            var signOutElement = await TryDirectSignOutSelectors(page);
            if (signOutElement != null)
            {
                return signOutElement;
            }

            // If not found, try user menu approach
            return await TryUserMenuSignOut(page);
        }

        /// <summary>
        /// Tries to find sign out button using direct selectors.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <returns>The sign out element if found, null otherwise</returns>
        private async Task<IElementHandle?> TryDirectSignOutSelectors(IPage page)
        {
            var signOutSelectors = new[]
            {
                "button[aria-label*='sign out' i]", "button[aria-label*='logout' i]",
                "a[aria-label*='sign out' i]", "a[aria-label*='logout' i]",
                "button:has-text('Sign Out')", "button:has-text('Logout')",
                "a:has-text('Sign Out')", "a:has-text('Logout')",
                "[data-testid*='signout']", "[data-testid*='logout']",
                "button[title*='sign out' i]", "button[title*='logout' i]",
                "a[title*='sign out' i]", "a[title*='logout' i]"
            };

            foreach (var selector in signOutSelectors)
            {
                try
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null)
                    {
                        _logger.LogLine($"Found sign out element using selector: {selector}");
                        return element;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"Selector '{selector}' failed: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to find sign out through user menu dropdowns.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <returns>The sign out element if found, null otherwise</returns>
        private async Task<IElementHandle?> TryUserMenuSignOut(IPage page)
        {
            _logger.LogLine("Sign out button not found, trying to find user menu...");
            
            var userMenuSelectors = new[]
            {
                "button[aria-label*='user' i]", "button[aria-label*='profile' i]", "button[aria-label*='account' i]",
                "[data-testid*='user']", "[data-testid*='profile']", "[data-testid*='account']",
                ".user-menu", ".profile-menu", ".account-menu"
            };

            foreach (var selector in userMenuSelectors)
            {
                var signOutElement = await TryClickUserMenuAndFindSignOut(page, selector);
                if (signOutElement != null)
                {
                    return signOutElement;
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to click a user menu element and find sign out in the dropdown.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="selector">The selector for the user menu</param>
        /// <returns>The sign out element if found, null otherwise</returns>
        private async Task<IElementHandle?> TryClickUserMenuAndFindSignOut(IPage page, string selector)
        {
            try
            {
                var userMenuElement = await page.QuerySelectorAsync(selector);
                if (userMenuElement == null)
                {
                    return null;
                }

                _logger.LogLine($"Found user menu using selector: {selector}");
                await userMenuElement.ClickAsync();
                _logger.LogLine("Clicked user menu, waiting for dropdown...");
                
                await Task.Delay(1000);
                
                // Try to find sign out in the dropdown
                return await TryDirectSignOutSelectors(page);
            }
            catch (Exception ex)
            {
                _logger.LogLine($"User menu selector '{selector}' failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clicks the sign out button and waits for the action to complete.
        /// </summary>
        /// <param name="signOutElement">The sign out element to click</param>
        private async Task ClickSignOutButton(IElementHandle signOutElement)
        {
            _logger.LogLine("Clicking sign out button...");
            await signOutElement.ClickAsync();
            
            // Wait for sign out to complete
            await Task.Delay(2000);
            _logger.LogLine("Sign out completed");
        }

        /// <summary>
        /// Waits for the download to complete and returns the video data.
        /// </summary>
        /// <param name="downloadDirectory">The download directory to monitor</param>
        /// <returns>The downloaded video data as byte array</returns>
        private async Task<byte[]> WaitForDownloadAndGetVideoData(string downloadDirectory)
        {
            _logger.LogLine("Waiting for video download to complete...");

            var initialFileCount = Directory.GetFiles(downloadDirectory, "*.mp4").Length;
            var maxWaitTime = TimeSpan.FromSeconds(MaxVideoDownloadWaitTimeSeconds);
            var checkInterval = TimeSpan.FromSeconds(1);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogLine($"Initial file count: {initialFileCount}");

            while (stopwatch.Elapsed < maxWaitTime)
            {
                var currentFileCount = Directory.GetFiles(downloadDirectory, "*.mp4").Length;
                if (currentFileCount > initialFileCount)
                {
                    _logger.LogLine($"New video file detected after {stopwatch.Elapsed.TotalSeconds:F1} seconds");
                    await Task.Delay(2000);
                    break;
                }

                var partialFiles = Directory.GetFiles(downloadDirectory, "*.crdownload").Length;
                var tempFiles = Directory.GetFiles(downloadDirectory, "*.tmp").Length;

                if (partialFiles > 0 || tempFiles > 0)
                {
                    _logger.LogLine($"Partial download files detected: .crdownload={partialFiles}, .tmp={tempFiles}");
                }

                await Task.Delay(checkInterval);
            }

            var videoFiles = Directory.GetFiles(downloadDirectory, "*.mp4");
            _logger.LogLine($"Found {videoFiles.Length} video files in the current directory.");

            var latestVideoFile = videoFiles
                .OrderByDescending(f => File.GetCreationTime(f))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(latestVideoFile))
            {
                _logger.LogLine("No video files found in download directory");
                throw new FileNotFoundException("No video files were downloaded");
            }

            byte[] videoData = await File.ReadAllBytesAsync(latestVideoFile);
            _logger.LogLine($"Video data size: {videoData.Length} bytes");

            return videoData;
        }

        /// <summary>
        /// Processes download events from the browser.
        /// </summary>
        private void ProcessDownloadEvent(string messageId, JsonElement messageData, ref bool downloadStarted, ref string? downloadGuid)
        {
            if (messageId == "Browser.downloadWillBegin")
            {
                downloadStarted = true;
                _logger.LogLine("Download event detected: Browser.downloadWillBegin");
                ProcessDownloadBeginEvent(messageData, ref downloadGuid);
            }
            else if (messageId == "Browser.downloadProgress")
            {
                ProcessDownloadProgressEvent(messageData);
            }
        }

        /// <summary>
        /// Processes the download begin event to extract GUID information.
        /// </summary>
        private void ProcessDownloadBeginEvent(JsonElement data, ref string? downloadGuid)
        {
            if (data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty("guid", out var guidElement))
            {
                downloadGuid = guidElement.GetString();
                _logger.LogLine($"Download started with GUID: {downloadGuid}");
            }
        }

        /// <summary>
        /// Processes the download progress event to track completion status.
        /// </summary>
        private void ProcessDownloadProgressEvent(JsonElement data)
        {
            if (data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty("state", out var stateElement))
            {
                var state = stateElement.GetString();
                _logger.LogLine($"Download progress: {state}");
                if (state == "completed")
                {
                    _logger.LogLine("Download completed via event notification");
                }
            }
        }

        /// <summary>
        /// Cleans up the event handler to prevent disposed object access issues.
        /// </summary>
        private async Task CleanupEventHandler(IPage page, EventHandler<MessageEventArgs>? downloadEventHandler)
        {
            if (downloadEventHandler != null)
            {
                try
                {
                    page.Client.MessageReceived -= downloadEventHandler;
                    _logger.LogLine("Download event handler cleaned up");
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"Warning: Error cleaning up event handler: {ex.Message}");
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Checks if an exception is related to disposed object access.
        /// </summary>
        private static bool IsDisposedObjectError(Exception ex)
        {
            var errorMessage = ex.Message?.ToLower() ?? string.Empty;
            return errorMessage.Contains("cannot access a disposed object") || 
                   errorMessage.Contains("disposed") ||
                   ex is ObjectDisposedException;
        }

        /// <summary>
        /// Uploads a screenshot to S3 using consistent naming with event/video keys.
        /// </summary>
        /// <param name="screenshotPath">Local path to the screenshot file</param>
        /// <param name="fileName">Base name for the file (e.g., "login-screenshot.png")</param>
        /// <param name="trigger">The trigger information for consistent naming</param>
        /// <param name="timestamp">The event timestamp</param>
        private async Task UploadScreenshotToS3(string screenshotPath, string fileName, Trigger trigger, long timestamp)
        {
            try
            {
                if (!File.Exists(screenshotPath))
                {
                    _logger.LogLine($"Screenshot file not found for upload: {screenshotPath}");
                    return;
                }

                // Generate S3 key for screenshots in the screenshots folder with date subfolder
                DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                string dateFolder = $"{dt.Year}-{dt.Month:D2}-{dt.Day:D2}";
                string basePrefix = $"{trigger.eventId}_{trigger.device}_{timestamp}";
                
                // Extract file extension from fileName
                string extension = Path.GetExtension(fileName);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                
                var s3Key = $"screenshots/{dateFolder}/{basePrefix}_{baseName}{extension}";
                
                _logger.LogLine($"Uploading screenshot to S3: {s3Key}");
                
                // Use the screenshot-specific upload method with proper content type
                await _s3StorageService.StoreScreenshotFileAsync(screenshotPath, s3Key);
                
                _logger.LogLine($"Screenshot successfully uploaded to S3: {s3Key}");
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error uploading screenshot to S3: {ex.Message}");
                // Don't throw - screenshot upload failure shouldn't break the main process
            }
        }

        #endregion
    }
}
