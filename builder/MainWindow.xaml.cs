// RECOVERED by decompilation (comments/formatting lost; logic intact).
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using builder.Audio;
using builder.Models;
using builder.Physics;
using builder.Scripting;
using builder.Settings;
using builder.Viewport;
using Fluent;
using Microsoft.Win32;
using OpenTK.Mathematics;
using OpenTK.Wpf;

#nullable disable // recovered by decompilation: silences decompiler-induced nullability noise
namespace builder;

public partial class MainWindow : RibbonWindow
{
	private struct WinPoint
	{
		public int X;

		public int Y;
	}

	private sealed class SceneSnapshot
	{
		public string Label = "";

		public string MergeKey = "";

		public List<SceneObject> Parts = new List<SceneObject>();

		public int SelectedIndex = -1;

		public List<int> MemberIndices = new List<int>();

		public int PartCounter;

		public OpenTK.Mathematics.Vector3 SunDirection;

		public OpenTK.Mathematics.Vector3 SunColor;

		public float SunIntensity;

		public bool Shadows;

		public float Ambient;

		public float FogDensity;

		public OpenTK.Mathematics.Vector3 FogColor;

		public float Haze;

		public float Time;

		public OpenTK.Mathematics.Vector3 SkyTint;

		public float StarAmount;

		public float CloudAmount;

		public float SunSize;

		public bool SunStripes;

		public int SkyStyle;

		public string SkyPresetName = "Clear Blue";
	}

	private enum ColorMode
	{
		R,
		G,
		B,
		H,
		S,
		V
	}

	private readonly StudioScene _scene = new StudioScene();

	private readonly HashSet<SceneObject> _selection = new HashSet<SceneObject>();

	private readonly PhysicsWorld _physics = new PhysicsWorld();

	private bool _sceneReady;

	private string _tool = "Move";

	private ScriptScheduler? _scripts;

	private AvatarRig? _avatarRig;

	private GridLength _rightWidth = new GridLength(300.0);

	private GridLength _propsRowH = new GridLength(1.4, GridUnitType.Star);

	private DateTime _lastFpsPush = DateTime.MinValue;

	private DateTime _lastPulseBeat = DateTime.MinValue;

	private bool _glStarted;

	private TimeSpan? _lastPumpTime;

	private bool _vsync = true;

	private double _refreshHz = 60.0;

	private TimeSpan? _lastTick;

	private bool _needsFrame = true;

	private bool _shadowFrameFlip;
	private OpenTK.Mathematics.Vector3 _lastShadowAvatarPos = new OpenTK.Mathematics.Vector3(float.MaxValue, 0f, 0f);

	/// <summary>True when play motion could have moved a shadow caster: the
	/// avatar displaced (catches teleports, which zero velocity), any body
	/// actually moving, or kinematic teleporters (movers/spinners pose-hop
	/// with ~zero velocity). Blink flips invalidate at flip time.</summary>
	private bool PlayCastersMoving()
	{
		Viewport.SceneObject player = _physics.Player;
		if (player != null)
		{
			if ((player.Position - _lastShadowAvatarPos).LengthSquared > 1e-06f)
			{
				_lastShadowAvatarPos = player.Position;
				return true;
			}
		}
		if (_physics.AnyBodyMoving(0.1f))
		{
			return true;
		}
		foreach (Viewport.SceneObject item in _scene.Objects)
		{
			if (item.Hidden)
			{
				continue;
			}
			if (item.IsMovingPlatform || item.IsSpinner)
			{
				return true;
			}
		}
		return false;
	}

	private DateTime _lastFrameUtc;

	private DateTime _lastPropPush = DateTime.MinValue;

	private DateTime _lastHoverCheck = DateTime.MinValue;

	private System.Windows.Point _lastMouse;

	private bool _lookHeld;

	private bool _panHeld;

	private bool _orbitHeld;

	private string? _faceOpenPath;

	private string? _faceBlinkPath;

	private readonly Dictionary<SceneObject, SceneObject> _npcFaces = new Dictionary<SceneObject, SceneObject>();

	private readonly Dictionary<SceneObject, Border> _npcBubbleViews = new Dictionary<SceneObject, Border>();

	private bool _playerMode;

	private bool _paused;

	private DateTime _playStartUtc;

	private int _dragAxis = -1;

	private bool _objectDrag;

	private bool _marqueeActive;

	private System.Windows.Point _marqueeStart;

	private bool _marqueeAdditive;

	private OpenTK.Mathematics.Vector3 _objectDragOffset;

	private OpenTK.Mathematics.Vector3 _dragOffset = OpenTK.Mathematics.Vector3.Zero;

	private OpenTK.Mathematics.Vector3 _dragStartPos;

	private OpenTK.Mathematics.Vector3 _dragSize;

	private float _dragCoord0;

	private float _dragSign = 1f;

	private float _scaleRawS;

	private bool _scaleSymmetric;

	private bool _scaleUniform;

	private TimeSpan? _prevPumpTime;

	private OpenTK.Mathematics.Vector3 _dragR0;

	private float _rotateAccum;

	private readonly Dictionary<SceneObject, (OpenTK.Mathematics.Vector3 P, OpenTK.Mathematics.Quaternion Q)> _dragPose = new Dictionary<SceneObject, (OpenTK.Mathematics.Vector3, OpenTK.Mathematics.Quaternion)>();

	private SettingsWindow? _settingsWin;

	private WaypointEditorWindow? _waypointWin;

	private Rect _winBounds;

	private bool _winBoundsSaved;

	private ObservableCollection<SceneNode> _explorerRoots = new ObservableCollection<SceneNode>();

	private SceneNode _workspace = new SceneNode();

	private int _explorerObjectCount = -1;

	private SceneObject? _liveObj;

	private List<PropertyRow>? _liveRows;

	private string? _liveService;

	private bool _syncingTree;

	private readonly Dictionary<SceneObject, OpenTK.Mathematics.Vector3> _dragSizes = new Dictionary<SceneObject, OpenTK.Mathematics.Vector3>();

	private readonly Dictionary<SceneObject, OpenTK.Mathematics.Vector3> _dragBottoms = new Dictionary<SceneObject, OpenTK.Mathematics.Vector3>();

	private OpenTK.Mathematics.Vector3 _rotatePivot;

	private List<PropertyRow> _propFull = new List<PropertyRow>();

	private bool _propFilterSync;

	private readonly HashSet<string> _collapsedCats = new HashSet<string>(StringComparer.Ordinal);

	private SceneSnapshot? _colorBefore;

	private bool _colorLiveChanged;

	private double _customH;

	private double _customS = 1.0;

	private double _customV = 1.0;

	private int _partCounter;

	private static readonly Color4[] Palette = new Color4[6]
	{
		new Color4(0.25f, 0.55f, 0.95f, 1f),
		new Color4(0.95f, 0.55f, 0.2f, 1f),
		new Color4(0.3f, 0.8f, 0.35f, 1f),
		new Color4(0.7f, 0.4f, 0.9f, 1f),
		new Color4(0.95f, 0.3f, 0.3f, 1f),
		new Color4(0.25f, 0.85f, 0.85f, 1f)
	};

	private const float VoidDeleteY = -300f;

	private DateTime _lastVoidSweep = DateTime.MinValue;

	private readonly List<SceneObject> _voidDeleted = new List<SceneObject>();

	private const float ContactSlop = 0.0001f;

	private OpenTK.Mathematics.Vector3? _toolboxDropPoint;

	private int _teleportCounter;

	private float _lastHealthShown = -1f;

	private DateTime _damageFlashStartUtc = DateTime.MinValue;

	private readonly List<(PhysicsWorld.NpcBubble Bubble, float Key)> _bubbleWork = new List<(PhysicsWorld.NpcBubble, float)>();

	private readonly HashSet<SceneObject> _bubbleSeen = new HashSet<SceneObject>();

	private readonly List<SceneObject> _staleBubbleKeys = new List<SceneObject>();

	private readonly List<SceneSnapshot> _undo = new List<SceneSnapshot>();

	private readonly List<SceneSnapshot> _redo = new List<SceneSnapshot>();

	private string? _lastUndoKey;

	private DateTime _lastUndoTime;

	private bool _restoringHistory;

	private SceneSnapshot? _dragBefore;

	private bool _dragMoved;

	private string? _savePath;

	private bool _dirty;

	private static readonly JsonSerializerOptions PlaceJson = new JsonSerializerOptions
	{
		WriteIndented = true,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	internal static readonly HashSet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".aiff", ".aif" };

	private GridLength _leftWidth = new GridLength(280.0);

	private GridLength _toolboxRowH = new GridLength(1.0, GridUnitType.Star);

	private string _skyPresetName = "Clear Blue";

	private bool _applyingSky;

	private ColorMode _colorMode = ColorMode.H;

	private bool _mapsDirty = true;

	private ColorMode _rMode = ColorMode.H;

	private double _rH;

	private double _rS;

	private double _rV;

	private double _rR;

	private double _rG;

	private double _rB;

	private static readonly Dictionary<string, ImageSource> _thumbCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> BulkEditRows = new HashSet<string>(StringComparer.Ordinal)
	{
		"Color", "Material", "Transparency", "Reflectance", "Reflection", "CastShadow", "Anchored", "CanCollide", "Mass", "Direction",
		"Speed", "Speeding Mode", "Volume", "Looped", "Playing", "Locked"
	};

	private static IReadOnlyList<string>? _fontNames;






























































































	public static string ModelsFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuilderStudio", "Models");

	public string SkyPresetName => _skyPresetName;

	private static string EngineIconPath => System.IO.Path.Combine(AppContext.BaseDirectory, "AppIcons", "engineIcon.ico");

	private static string PublishedIconPath => System.IO.Path.Combine(AppContext.BaseDirectory, "AppIcons", "publishedIcon.ico");

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out WinPoint lpPoint);

	[DllImport("user32.dll")]
	private static extern bool SetCursorPos(int X, int Y);

	public MainWindow()
	{
		CrashLog.Startup("MainWindow ctor begin");
		InitializeComponent();
		CrashLog.Startup("InitializeComponent done");
		ApplyWindowIcon();
		ApplyAllSettings();
		TimeSlider.Value = AppSettings.Current.TimeOfDay;
		_explorerRoots = SceneNode.CreateDefaultWorkspace();
		_workspace = _explorerRoots.First((SceneNode r) => r.Name == "World");
		ExplorerTree.ItemsSource = _explorerRoots;
		ToolboxTabs.Main = this;
		RefreshExplorer();
		SetPropertyRows(DefaultProperties("World", "World"));
		BuildSwatches();
		BuildColorMaps();
		BuildMaterials();
		BuildTemplateList();
		AudioEngine.Logger = Log;
		StudioScene.MeshLog = Log;
		ViewportControl.PreviewMouseDown += Viewport_MouseDown;
		ViewportControl.PreviewMouseMove += Viewport_MouseMove;
		ViewportControl.PreviewMouseUp += Viewport_MouseUp;
		ViewportControl.MouseWheel += Viewport_Wheel;
		ViewportControl.PreviewKeyDown += Viewport_KeyDown;
		base.PreviewKeyDown += MainWindow_PreviewKeyDown;
		ExplorerTree.PreviewMouseLeftButtonDown += ExplorerTree_PreviewMouseDown;
		ViewportControl.LostKeyboardFocus += delegate
		{
			CancelCameraDrag();
		};
		ViewportControl.LostMouseCapture += delegate
		{
			CancelCameraDrag();
		};
		base.Loaded += OnLoadedPlayerCheck;
		AppSettings current = AppSettings.Current;
		CrashLog.Startup($"GL start begin ({current.GlMajor}.{current.GlMinor})");
		if (!TryStartGL(current.GlMajor, current.GlMinor) && current.GlMajor == 4 && current.GlMinor == 6)
		{
			CrashLog.Startup("GL 4.6 failed, showing fallback prompt");
			if (new RenderFallbackWindow().ShowDialog() == true)
			{
				CrashLog.Startup("fallback accepted, saving 4.0 and restarting");
				AppSettings.Current.GlMajor = 4;
				AppSettings.Current.GlMinor = 0;
				AppSettings.Current.Save();
				MessageBox.Show("builder will now restart to switch the rendering system to OpenGL 4.", "builder engine", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				Process.Start(new ProcessStartInfo
				{
					FileName = Environment.ProcessPath,
					UseShellExecute = true
				});
				Application.Current.Shutdown();
				return;
			}
			CrashLog.Startup("fallback declined");
		}
		CrashLog.Startup($"GL started={_glStarted}");
		if (!_glStarted)
		{
			StatusText.Text = "Viewport unavailable: no supported OpenGL context.";
			ViewportTitle.Text = "Viewport unavailable";
			ToolLabel.Text = "Rendering system failed to start \u2014 see Output.";
		}
		CompositionTarget.Rendering += PumpViewport;
		base.Closing += OnClosing;
		base.Loaded += async delegate
		{
			await CheckForUpdatesAsync();
		};
		_dirty = false;
		UpdateTitle();
		UpdateUndoButtons();
		CrashLog.Startup("MainWindow ctor end");
	}

	private bool TryStartGL(int major, int minor)
	{
		try
		{
			GLWpfControlSettings gLWpfControlSettings = ViewportControl.Settings?.Clone() ?? new GLWpfControlSettings();
			gLWpfControlSettings.MajorVersion = major;
			gLWpfControlSettings.MinorVersion = minor;
			gLWpfControlSettings.RenderContinuously = true;
			gLWpfControlSettings.Samples = (AppSettings.Current.Msaa ? 4 : 0);
			ViewportControl.Start(gLWpfControlSettings);
			_glStarted = true;
			StudioScene.GlslVersion = major * 100 + minor * 10;
			Log($"GLWpfControl started (OpenGL {major}.{minor}).");
			return true;
		}
		catch (Exception ex)
		{
			Log($"Viewport start failed (OpenGL {major}.{minor}): " + ex.Message);
			StatusText.Text = $"Viewport failed (OpenGL {major}.{minor}): " + ex.Message;
			return false;
		}
	}

	public void TestFallbackPrompt()
	{
		bool valueOrDefault = new RenderFallbackWindow
		{
			Owner = this
		}.ShowDialog() == true;
		Log("Fallback prompt test: " + (valueOrDefault ? "OK" : "Cancel") + ".");
		StatusText.Text = "Fallback test: " + (valueOrDefault ? "OK" : "Cancel");
	}

	public static bool TryParseUpdateVersion(string? tag, out Version? version)
	{
		version = null;
		if (string.IsNullOrWhiteSpace(tag))
		{
			return false;
		}
		string text = tag.Trim().TrimStart('v', 'V');
		int num = text.IndexOfAny(new char[2] { '-', '+' });
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		if (!Version.TryParse(text, out Version result))
		{
			return false;
		}
		version = result;
		return true;
	}

	public static bool TryParseUpdateManifest(string json, out string version, out string url, out string sha256, out string signature, out string notes)
	{
		version = "";
		url = "";
		sha256 = "";
		signature = "";
		notes = "";
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("version", out var value) || !rootElement.TryGetProperty("url", out var value2) || !rootElement.TryGetProperty("sha256", out var value3) || !rootElement.TryGetProperty("signature", out var value4))
			{
				return false;
			}
			version = (value.GetString() ?? "").Trim();
			url = (value2.GetString() ?? "").Trim();
			sha256 = (value3.GetString() ?? "").Trim().ToLowerInvariant();
			signature = (value4.GetString() ?? "").Trim();
			if (rootElement.TryGetProperty("notes", out var value5))
			{
				notes = value5.GetString() ?? "";
			}
			Uri result;
			return version.Length > 0 && Uri.TryCreate(url, UriKind.Absolute, out result) && result.Scheme == Uri.UriSchemeHttps && Regex.IsMatch(sha256, "^[0-9a-f]{64}$") && signature.Length > 0;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public void CheckForUpdatesNow()
	{
		_ = CheckForUpdatesAsync(manual: true);
	}

	private async Task CheckForUpdatesAsync(bool manual = false)
	{
		string text = (AppSettings.Current.UpdateManifestUrl ?? "").Trim();
		if (!manual && !AppSettings.Current.CheckForUpdates)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			if (manual)
			{
				Log("Updates: paste the update link (manifest URL) in Settings first.");
				StatusText.Text = "Set update link first";
			}
		}
		else
		{
			if (!manual && (DateTime.UtcNow - AppSettings.Current.LastUpdateCheckUtc).TotalHours < 24.0)
			{
				return;
			}
			try
			{
				using HttpClient http = new HttpClient
				{
					Timeout = TimeSpan.FromSeconds(15L)
				};
				http.DefaultRequestHeaders.UserAgent.ParseAdd("builder-updater");
				if (!TryParseUpdateManifest(await http.GetStringAsync(text), out string version, out string url, out string sha, out string signature, out string notes))
				{
					Log("Updates: couldn't read the signed update manifest.");
					if (manual)
					{
						StatusText.Text = "Bad update link";
					}
					return;
				}
				if (!UpdateSignature.IsConfigured)
				{
					Log("Updates: signing key is not configured in this build.");
					if (manual)
					{
						StatusText.Text = "Updates not configured";
					}
					return;
				}
				if (!UpdateSignature.Verify(version, url, sha, notes, signature))
				{
					Log("Updates: manifest signature verification failed.");
					if (manual)
					{
						StatusText.Text = "Untrusted update manifest";
					}
					return;
				}
				if (!TryParseUpdateVersion(version, out Version version2) || version2 == null)
				{
					Log("Updates: bad version in manifest ('" + version + "').");
					return;
				}
				AppSettings.Current.LastUpdateCheckUtc = DateTime.UtcNow;
				AppSettings.Current.Save();
				Version version3 = Assembly.GetExecutingAssembly().GetName().Version;
				if (version3 != null && version2 <= version3)
				{
					if (manual)
					{
						Log($"Updates: already on latest ({version3}).");
						StatusText.Text = "Already up to date";
					}
					return;
				}
				if (notes.Length > 400)
				{
					notes = notes.Substring(0, 400) + "\u2026";
				}
				if (new ConfirmWindow
				{
					Owner = this,
					Message = $"builder {version2} is available (you have {version3}). Update now? The app will restart." + (string.IsNullOrWhiteSpace(notes) ? "" : ("\n\n" + notes))
				}.ShowDialog() != true)
				{
					Log("Updates: skipped " + version + ".");
					return;
				}
				await DownloadAndApplyAsync(url, version2.ToString(), sha);
			}
			catch (Exception ex)
			{
				Log("Update check failed: " + ex.Message);
				if (manual)
				{
					StatusText.Text = "Update check failed";
				}
			}
		}
	}

	private async Task DownloadAndApplyAsync(string url, string version, string expectedSha256)
	{
		StatusText.Text = "Downloading builder " + version + "...";
		Log("Updates: downloading " + version + ".");
		string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "builder-update", version);
		Directory.CreateDirectory(tmp);
		string zip = System.IO.Path.Combine(tmp, "update.zip");
		string text = null;
		using (HttpClientHandler handler = new HttpClientHandler
		{
			AllowAutoRedirect = true,
			CookieContainer = new CookieContainer()
		})
		{
			using HttpClient http = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromMinutes(10L)
			};
			http.DefaultRequestHeaders.UserAgent.ParseAdd("builder-updater");
			text = await DownloadUrlAsync(http, url, zip);
			if (!LooksLikeZip(zip))
			{
				string text2 = FindDriveConfirmUrl(await File.ReadAllTextAsync(zip));
				if (text2 == null)
				{
					throw new InvalidDataException("the update link didn't return a zip file. Fix the manifest: the url must be a direct download link (https://drive.google.com/uc?export=download&id=FILE_ID), shared as Anyone with the link.");
				}
				Log("Updates: confirming Drive download...");
				text = await DownloadUrlAsync(http, text2, zip);
			}
		}
		long length = new FileInfo(zip).Length;
		Log($"Updates: downloaded {length} bytes ({text ?? "unknown type"}).");
		if (!LooksLikeZip(zip))
		{
			throw new InvalidDataException($"the update link didn't return a zip file (got {length} bytes of {text ?? "unknown type"}). " + "Fix the manifest: the url must be a direct download link (https://drive.google.com/uc?export=download&id=FILE_ID), shared as Anyone with the link.");
		}
		if (!MatchesSha256(zip, expectedSha256))
		{
			throw new InvalidDataException("the downloaded update does not match the signed SHA-256 hash.");
		}
		string text3 = System.IO.Path.Combine(tmp, "app");
		if (Directory.Exists(text3))
		{
			Directory.Delete(text3, recursive: true);
		}
		ZipFile.ExtractToDirectory(zip, text3);
		string text4 = System.IO.Path.Combine(AppContext.BaseDirectory, "builder.exe");
		string text5 = System.IO.Path.Combine(tmp, "apply.bat");
		int processId = Environment.ProcessId;
		File.WriteAllText(text5, "@echo off\r\n" + $":wait\r\ntasklist /FI \"PID eq {processId}\" 2>NUL | find \"{processId}\" >NUL\r\n" + "if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" + $"xcopy \"{text3}\\*\" \"{AppContext.BaseDirectory}\" /E /Y /Q\r\n" + "start \"\" \"" + text4 + "\"\r\nrmdir /S /Q \"" + tmp + "\"\r\n");
		Log("Updates: " + version + " staged, restarting to apply.");
		Process.Start(new ProcessStartInfo
		{
			FileName = text5,
			UseShellExecute = true,
			CreateNoWindow = true
		});
		Application.Current.Shutdown();
	}

	private static async Task<string?> DownloadUrlAsync(HttpClient http, string url, string dest)
	{
		using HttpResponseMessage resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		resp.EnsureSuccessStatusCode();
		string ct = resp.Content.Headers.ContentType?.ToString();
		string result;
		await using (FileStream fs = File.Create(dest))
		{
			await (await resp.Content.ReadAsStreamAsync()).CopyToAsync(fs);
			result = ct;
		}
		return result;
	}

	private static bool LooksLikeZip(string path)
	{
		try
		{
			byte[] array = new byte[4];
			using FileStream fileStream = File.OpenRead(path);
			if (fileStream.Length < 4)
			{
				return false;
			}
			fileStream.ReadExactly(array, 0, 4);
			return array[0] == 80 && array[1] == 75;
		}
		catch
		{
			return false;
		}
	}

	private static bool MatchesSha256(string path, string expected)
	{
		using FileStream source = File.OpenRead(path);
		return string.Equals(Convert.ToHexString(SHA256.HashData(source)), expected, StringComparison.OrdinalIgnoreCase);
	}

	internal static string? FindDriveConfirmUrl(string page)
	{
		RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Singleline;
		foreach (Match item2 in Regex.Matches(page, "<form[^>]*action\\s*=\\s*\"([^\"]+)\"[^>]*>(.*?)</form>", options))
		{
			string text = WebUtility.HtmlDecode(item2.Groups[1].Value);
			List<string> list = new List<string>();
			bool flag = false;
			bool flag2 = false;
			foreach (Match item3 in Regex.Matches(item2.Groups[2].Value, "<input[^>]*>", options))
			{
				Match match2 = Regex.Match(item3.Value, "name\\s*=\\s*\"([^\"]+)\"", options);
				if (match2.Success)
				{
					Match match3 = Regex.Match(item3.Value, "value\\s*=\\s*\"([^\"]*)\"", options);
					string value = match2.Groups[1].Value;
					string stringToEscape = (match3.Success ? match3.Groups[1].Value : "");
					if (value.Equals("id", StringComparison.OrdinalIgnoreCase))
					{
						flag = true;
					}
					if (value.Equals("confirm", StringComparison.OrdinalIgnoreCase))
					{
						flag2 = true;
					}
					string item = Uri.EscapeDataString(value) + "=" + Uri.EscapeDataString(stringToEscape);
					if (!list.Contains(item))
					{
						list.Add(item);
					}
				}
			}
			if (flag && flag2 && list.Count > 0)
			{
				if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				{
					text = "https://drive.google.com" + text;
				}
				return text + (text.Contains('?') ? "&" : "?") + string.Join("&", list);
			}
		}
		foreach (Match item4 in Regex.Matches(page, "\"((?:https://drive\\.google\\.com)?/uc\\?export=download[^\"]*?)\""))
		{
			string text2 = item4.Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
			if (text2.Contains("confirm=", StringComparison.OrdinalIgnoreCase))
			{
				return text2.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text2 : ("https://drive.google.com" + text2);
			}
		}
		return null;
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_playerMode)
		{
			ConfirmWindow confirmWindow = new ConfirmWindow
			{
				Owner = this
			};
			if (_dirty)
			{
				confirmWindow.Message = "Are you sure you want to exit builder? Any unsaved changes will be lost...";
			}
			if (confirmWindow.ShowDialog() != true)
			{
				e.Cancel = true;
				return;
			}
		}
		_settingsWin?.CloseImmediate();
		CompositionTarget.Rendering -= PumpViewport;
		_physics.Dispose();
		_scene.Dispose();
		AudioEngine.Shutdown();
	}

	private void PumpViewport(object? sender, EventArgs e)
	{
		if (!_glStarted || !ViewportControl.IsVisible || !(e is RenderingEventArgs { RenderingTime: var renderingTime } e2))
		{
			return;
		}
		TimeSpan? lastPumpTime = _lastPumpTime;
		if (renderingTime == lastPumpTime)
		{
			return;
		}
		_lastPumpTime = e2.RenderingTime;
		if (_prevPumpTime.HasValue)
		{
			Math.Clamp((e2.RenderingTime - _prevPumpTime.Value).TotalSeconds, 0.0, 0.1);
		}
		_prevPumpTime = e2.RenderingTime;
		if (_lastTick.HasValue)
		{
			double totalSeconds = (e2.RenderingTime - _lastTick.Value).TotalSeconds;
			if (totalSeconds > 0.0005 && totalSeconds < 0.5)
			{
				_refreshHz += (1.0 / totalSeconds - _refreshHz) * 0.05;
			}
		}
		_lastTick = e2.RenderingTime;
		if (_physics.Simulating && _physics.Player != null)
		{
			if (ViewportControl.IsKeyboardFocused)
			{
				_physics.PlayerMoveX = (Keyboard.IsKeyDown(Key.D) ? 1 : 0) - (Keyboard.IsKeyDown(Key.A) ? 1 : 0);
				_physics.PlayerMoveZ = (Keyboard.IsKeyDown(Key.W) ? 1 : 0) - (Keyboard.IsKeyDown(Key.S) ? 1 : 0);
				_physics.PlayerCamYaw = _scene.Yaw;
				_physics.JumpHeld = Keyboard.IsKeyDown(Key.Space);
			}
			else
			{
				_physics.PlayerMoveX = 0f;
				_physics.PlayerMoveZ = 0f;
				_physics.JumpHeld = false;
			}
			if (_playerMode && _paused)
			{
				return;
			}
			SceneObject faceObject = _physics.FaceObject;
			if (_faceOpenPath != null && _faceBlinkPath != null && faceObject != null && faceObject.Decals.Count > 0 && (faceObject.Decals[0].Image == _faceOpenPath || faceObject.Decals[0].Image == _faceBlinkPath))
			{
				bool flag = (DateTime.UtcNow - _playStartUtc).TotalSeconds % 3.4 < 0.15;
				faceObject.Decals[0].Image = (flag ? _faceBlinkPath : _faceOpenPath);
			}
			_scene.FlyInput = OpenTK.Mathematics.Vector3.Zero;
			_scene.FlySlow = false;
		}
		else if (ViewportControl.IsKeyboardFocused && !CtrlHeld())
		{
			float x = (Keyboard.IsKeyDown(Key.D) ? 1 : 0) - (Keyboard.IsKeyDown(Key.A) ? 1 : 0);
			float y = (Keyboard.IsKeyDown(Key.E) ? 1 : 0) - (Keyboard.IsKeyDown(Key.Q) ? 1 : 0);
			float z = (Keyboard.IsKeyDown(Key.W) ? 1 : 0) - (Keyboard.IsKeyDown(Key.S) ? 1 : 0);
			_scene.FlyInput = new OpenTK.Mathematics.Vector3(x, y, z);
			_scene.FlySlow = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
		}
		else
		{
			_scene.FlyInput = OpenTK.Mathematics.Vector3.Zero;
			_scene.FlySlow = false;
		}
		DateTime utcNow = DateTime.UtcNow;
		bool flag2 = _physics.Simulating || !_scene.IsIdle || _needsFrame || (utcNow - _lastFrameUtc).TotalMilliseconds > 500.0;
		if (!flag2 && !_physics.Simulating && !_needsFrame && !_scene.IsIdle && (utcNow - _lastPulseBeat).TotalMilliseconds > 250.0)
		{
			flag2 = true;
			_lastPulseBeat = utcNow;
		}
		_needsFrame = false;
		if (!flag2)
		{
			return;
		}
		_physics.Step(_scene);
		if (_physics.Simulating)
		{
			_scripts?.Tick();
		}
		if (_avatarRig != null && _physics.Player != null)
		{
			_physics.PlayerMotion(out var vx, out var vy, out var vz, out var grounded, out var swimming);
			double dt = Math.Clamp((utcNow - _lastFrameUtc).TotalSeconds, 0.0, 0.05);
			_avatarRig.Update(_physics.Player, _physics.FaceObject, vx, vy, vz, grounded, swimming, dt);
		}
		UpdatePartSounds();
		if (_npcFaces.Count > 0)
		{
			foreach (var (sceneObject3, sceneObject4) in _npcFaces.ToList())
			{
				if (!_scene.Objects.Contains(sceneObject3) || !_scene.Objects.Contains(sceneObject4))
				{
					_npcFaces.Remove(sceneObject3);
					continue;
				}
				OpenTK.Mathematics.Vector3 vec = new OpenTK.Mathematics.Vector3(0f, 0.2f, 0.38f);
				sceneObject4.Position = sceneObject3.Position + OpenTK.Mathematics.Vector3.Transform(vec, sceneObject3.Orientation);
				sceneObject4.Orientation = sceneObject3.Orientation;
			}
		}
		if (_physics.Simulating)
		{
			_shadowFrameFlip = !_shadowFrameFlip;
			if (_shadowFrameFlip && PlayCastersMoving())
			{
				_scene.InvalidateShadows();
			}
			if ((utcNow - _lastVoidSweep).TotalMilliseconds >= 250.0)
			{
				_lastVoidSweep = utcNow;
				SweepVoidFalls();
			}
			if (_physics.VoidDiedThisStep)
			{
				Log("Oof! You fell into the void.");
				StatusText.Text = "Oof! Fell into the void";
			}
			else if (_physics.DiedThisStep)
			{
				Log("Oof! The avatar died and respawned at full health.");
				StatusText.Text = "Oof! Respawned";
			}
			if (_physics.VoidRespawnedThisStep)
			{
				Log("Respawned at full health.");
				StatusText.Text = "Respawned";
			}
			else if (_physics.IsVoidDead)
			{
				StatusText.Text = $"Respawning in {_physics.VoidRespawnRemaining:0}s\u2026";
			}
			if (_physics.TeleportedThisStep)
			{
				Log("Teleported to " + _physics.LastTeleportName + "!");
				StatusText.Text = "Teleported to " + _physics.LastTeleportName;
			}
			if (_physics.CheckpointThisStep)
			{
				Log("Checkpoint: " + _physics.LastCheckpointName + "!");
				StatusText.Text = "Checkpoint: " + _physics.LastCheckpointName;
			}
			if (_physics.BouncedThisStep)
			{
				Log("Boing!");
				StatusText.Text = "Boing!";
				if (_physics.Player != null)
				{
					AudioEngine.PlayEffect("boing", 0.9f, _physics.Player.Position);
				}
			}
			SceneObject brokeObject = _physics.BrokeObject;
			if (brokeObject != null && _scene.Objects.Contains(brokeObject))
			{
				_physics.BrokeObject = null;
				RemovePlayPart(brokeObject);
				AudioEngine.PlayEffect("wood-breaking", 0.9f, brokeObject.Position);
				AfterPlayRemoval("Part shattered", new List<SceneObject> { brokeObject });
				Log($"Broke: {brokeObject.Name} shattered after {brokeObject.BreakCount} touches (comes back on Stop).");
			}
			UpdateHealthBar();
		}
		UpdateTimedParts((utcNow - DateTime.UnixEpoch).TotalSeconds);
		UpdateNpcBubbles();
		_lastFrameUtc = utcNow;
		ViewportControl.InvalidateVisual();
	}

	private void UpdatePartSounds()
	{
		if (!_physics.Simulating)
		{
			AudioEngine.StopAllPartSounds();
			return;
		}
		AudioEngine.SetListener(_scene.Position, _scene.Forward);
		foreach (SceneObject @object in _scene.Objects)
		{
			if (@object.SoundPlaying && @object.HasSound)
			{
				AudioEngine.PartSoundUpdate(@object, _scene.Position, SoundBaseDir());
			}
		}
		AudioEngine.ReapStale(_scene.Objects);
		AudioEngine.PollOneShots();
	}

	private void SweepVoidFalls()
	{
		SceneObject player = _physics.Player;
		List<SceneObject> doomed = null;
		foreach (SceneObject item in _scene.Objects.ToList())
		{
			if (!(item.Position.Y >= -300f) && item != player && !item.IsAvatarFace)
			{
				RemovePlayPart(item);
				(doomed ?? (doomed = new List<SceneObject>())).Add(item);
			}
		}
		if (doomed != null)
		{
			// One line per sweep (not per part): OutputBox churn is a hitch source.
			Log(doomed.Count == 1
				? $"Void: {doomed[0].Name} fell past y={-300f} and was deleted (comes back on Stop)."
				: $"Void: {doomed.Count} parts fell past y={-300f} and were deleted (come back on Stop).");
			AfterPlayRemoval("Part lost to the void", doomed);
		}
	}

	public SceneObject SpawnScriptPart(SceneObject o)
	{
		_partCounter++;
		if (string.IsNullOrWhiteSpace(o.Name))
		{
			o.Name = $"Script Part {_partCounter}";
		}
		_scene.Objects.Add(o);
		_physics.AddBody(o);
		_scene.InvalidateShadows();
		return o;
	}

	public void RemoveScriptPart(SceneObject o)
	{
		RemovePlayPart(o);
		AfterPlayRemoval("Removed by script", new List<SceneObject> { o });
	}

	private void RemovePlayPart(SceneObject o)
	{
		_physics.RemoveBody(o);
		_scene.Objects.Remove(o);
		_voidDeleted.Add(o);
		if (_selection.Remove(o) && _scene.Selected == o)
		{
			_scene.Selected = null;
			_liveObj = null;
			_liveRows = null;
			_liveService = null;
		}
	}

	private void AfterPlayRemoval(string status, List<SceneObject> removed = null)
	{
		_scene.SelectedObjects.RemoveWhere((SceneObject o) => !_scene.Objects.Contains(o));
		if (_scene.Selected == null && _selection.Count > 0)
		{
			_scene.Selected = _selection.FirstOrDefault();
		}
		SelectedLabel.Text = SelectionTitle();
		SetPropertyRows(_liveRows ?? new List<PropertyRow>());
		if (removed != null)
		{
			// Incremental path: only dead nodes drop (no tree rebuild), and the
			// depth map re-renders only when a real caster left with them.
			RemoveExplorerNodes(removed);
			bool casts = false;
			foreach (SceneObject o in removed)
			{
				if (!o.Hidden && o.Shape != ShapeKind.None && o.CastShadow
					&& o.Transparency < 0.99f && !MaterialParams.Of(o.Material).Transparent)
				{
					casts = true;
					break;
				}
			}
			if (casts)
			{
				_scene.InvalidateShadows();
			}
		}
		else
		{
			RefreshExplorer();
			_scene.InvalidateShadows();
		}
		_needsFrame = true;
		StatusText.Text = status;
	}

	private void UpdateTimedParts(double t)
	{
		if (_physics.Simulating)
		{
			return;
		}
		bool flag = false;
		foreach (SceneObject @object in _scene.Objects)
		{
			if (@object.IsTimedPart)
			{
				bool flag2 = @object.TimedVisibleAt(t);
				if (@object.Hidden == flag2)
				{
					@object.Hidden = !flag2;
					flag = true;
				}
			}
		}
		if (flag)
		{
			_scene.InvalidateShadows();
			_needsFrame = true;
		}
	}

	private void Viewport_Render(TimeSpan delta)
	{
		if (!_sceneReady)
		{
			try
			{
				_scene.Initialize();
				_sceneReady = true;
				_partCounter = _scene.Objects.Count;
				if (_scene.Selected != null)
				{
					_selection.Add(_scene.Selected);
				}
				AfterSelectionChanged();
				Log($"Scene ready: {_scene.Objects.Count} objects.");
				try
				{
					DateTime lastWriteTime = File.GetLastWriteTime(GetType().Assembly.Location);
					Log($"Build: {lastWriteTime:G}.");
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(_scene.InitLog))
				{
					Log("GL shaders:\n" + _scene.InitLog.TrimEnd());
				}
			}
			catch (Exception ex)
			{
				Log("Scene init failed: " + ex.Message);
				return;
			}
		}
		int num = ViewportControl.FrameBufferWidth;
		int num2 = ViewportControl.FrameBufferHeight;
		if (num <= 0 || num2 <= 0)
		{
			num = Math.Max(1, (int)ViewportControl.ActualWidth);
			num2 = Math.Max(1, (int)ViewportControl.ActualHeight);
		}
		_scene.Render(num, num2);
		DateTime utcNow = DateTime.UtcNow;
		if ((utcNow - _lastFpsPush).TotalMilliseconds > 250.0)
		{
			_lastFpsPush = utcNow;
			OpenTK.Mathematics.Vector3 position = _scene.Position;
			FpsText.Text = $"{_scene.Fps:0} FPS \u00B7 {_scene.CpuMs:0.0}ms";
			CamText.Text = $"Pos {position.X:0.0},{position.Y:0.0},{position.Z:0.0} yaw {_scene.Yaw:0}\u00B0 pitch {_scene.Pitch:0}\u00B0";
		}
	}

	private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
	{
		ViewportControl.Focus();
		_needsFrame = true;
		_lastMouse = e.GetPosition(ViewportControl);
		if (_physics.Simulating && _physics.Player != null)
		{
			if (e.RightButton == MouseButtonState.Pressed && !CtrlHeld())
			{
				_lookHeld = true;
				ViewportControl.Cursor = Cursors.None;
				System.Windows.Point point = ViewportCenterScreen();
				SetCursorPos((int)point.X, (int)point.Y);
				ViewportControl.CaptureMouse();
			}
			else if (e.LeftButton == MouseButtonState.Pressed && !CtrlHeld())
			{
				_orbitHeld = true;
				ViewportControl.CaptureMouse();
			}
			return;
		}
		if (e.RightButton == MouseButtonState.Pressed && !CtrlHeld())
		{
			_lookHeld = true;
			ViewportControl.Cursor = Cursors.None;
			System.Windows.Point point2 = ViewportCenterScreen();
			SetCursorPos((int)point2.X, (int)point2.Y);
		}
		if (e.MiddleButton == MouseButtonState.Pressed && !CtrlHeld())
		{
			_panHeld = true;
		}
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			System.Windows.Point position = e.GetPosition(ViewportControl);
			float num = (float)ViewportControl.ActualWidth;
			float num2 = (float)ViewportControl.ActualHeight;
			OpenTK.Mathematics.Vector3 ringPoint = OpenTK.Mathematics.Vector3.Zero;
			int num3 = ((_scene.ActiveGizmo == GizmoKind.None) ? (-1) : ((_scene.ActiveGizmo != GizmoKind.Rotate) ? _scene.PickGizmoAxis((float)position.X, (float)position.Y, num, num2, FrameH()) : _scene.PickRingAxis((float)position.X, (float)position.Y, num, num2, FrameH(), out ringPoint)));
			if (num3 >= 0 && _scene.Selected != null && !_scene.Selected.Locked)
			{
				_dragAxis = num3;
				_dragBefore = CaptureState("Drag " + _tool);
				_dragMoved = false;
				_rotatePivot = _scene.GizmoPivot;
				_rotateAccum = 0f;
				_dragPose.Clear();
				foreach (SceneObject item in DragTargets())
				{
					_dragPose[item] = (item.Position, item.Orientation);
				}
				_dragSizes.Clear();
				foreach (SceneObject item2 in DragTargets())
				{
					_dragSizes[item2] = item2.Size;
				}
				_scene.ActiveAxis = num3;
				_scene.GetPickRay((float)position.X, (float)position.Y, num, num2, out var origin, out var dir);
				OpenTK.Mathematics.Vector3 vector = _scene.AxisPoint(num3, origin, dir);
				_dragOffset = _scene.Selected.Position - vector;
				_dragStartPos = _scene.Selected.Position;
				_dragSize = _scene.Selected.Size;
				_dragCoord0 = OpenTK.Mathematics.Vector3.Dot(vector - _scene.Selected.Position, _scene.GizmoAxis(num3));
				_dragSign = Math.Sign(OpenTK.Mathematics.Vector3.Dot(vector - _scene.Selected.Position, _scene.GizmoAxis(num3)));
				if (_dragSign == 0f)
				{
					_dragSign = 1f;
				}
				_scaleRawS = _dragCoord0;
				_scaleSymmetric = CtrlHeld();
				_scaleUniform = AltHeld();
				_dragBottoms.Clear();
				foreach (SceneObject item3 in DragTargets())
				{
					_dragBottoms[item3] = BottomCenter(item3);
				}
				_dragR0 = ((_scene.ActiveGizmo == GizmoKind.Rotate) ? (ringPoint - _rotatePivot) : OpenTK.Mathematics.Vector3.Zero);
			}
			else
			{
				if (!_physics.Simulating && !_playerMode && num > 0f && num2 > 0f)
				{
					var (sceneObject, num4) = _scene.PickWaypoint((float)position.X, (float)position.Y, num, num2);
					if (sceneObject != null && num4 >= 0 && num4 < sceneObject.Waypoints.Count)
					{
						_scene.SelWaypointObj = sceneObject;
						_scene.SelWaypointIndex = num4;
						SelectWaypointNode(sceneObject, num4);
						StatusText.Text = $"Waypoint {num4 + 1} of {sceneObject.Name} selected";
						_needsFrame = true;
						return;
					}
				}
				SceneObject sceneObject2 = _scene.Pick((float)position.X, (float)position.Y, num, num2, skipLocked: true);
				bool flag = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
				if (flag)
				{
					ToggleObjectSelection(sceneObject2);
				}
				else
				{
					SelectObject(sceneObject2);
				}
				if (sceneObject2 == null && !flag && !_physics.Simulating && !_playerMode)
				{
					_marqueeActive = true;
					_marqueeStart = position;
					_marqueeAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
					ViewportControl.CaptureMouse();
				}
				if (_tool == "Move" && sceneObject2 != null && !flag && sceneObject2 != null)
				{
					_scene.GetPickRay((float)position.X, (float)position.Y, num, num2, out var origin2, out var dir2);
					bool flag2;
					OpenTK.Mathematics.Vector3 hit;
					OpenTK.Mathematics.Vector3 normal;
					if (AppSettings.Current.DragOnSurfaces)
					{
						flag2 = _scene.PickSurface((float)position.X, (float)position.Y, num, num2, new HashSet<SceneObject>(DragTargets()), out hit, out normal);
					}
					else
					{
						normal = _scene.Forward;
						flag2 = TryRayPlane(origin2, dir2, sceneObject2.Position, normal, out hit);
					}
					if (flag2)
					{
						_objectDrag = true;
						_dragBefore = CaptureState("Drag " + _tool);
						_dragMoved = false;
						OpenTK.Mathematics.Vector3 vector2 = sceneObject2.Size * 0.5f;
						float num5 = MathF.Abs(normal.X) * vector2.X + MathF.Abs(normal.Y) * vector2.Y + MathF.Abs(normal.Z) * vector2.Z;
						_objectDragOffset = sceneObject2.Position - (hit + normal * num5);
						_dragStartPos = sceneObject2.Position;
					}
				}
			}
		}
		if (_lookHeld || _panHeld || _dragAxis >= 0 || _objectDrag)
		{
			ViewportControl.CaptureMouse();
		}
	}

	private float FrameH()
	{
		return (ViewportControl.FrameBufferHeight > 0) ? ViewportControl.FrameBufferHeight : Math.Max(1, (int)ViewportControl.ActualHeight);
	}

	private static bool CtrlHeld()
	{
		if (!Keyboard.IsKeyDown(Key.LeftCtrl))
		{
			return Keyboard.IsKeyDown(Key.RightCtrl);
		}
		return true;
	}

	private static bool AltHeld()
	{
		if (!Keyboard.IsKeyDown(Key.LeftAlt))
		{
			return Keyboard.IsKeyDown(Key.RightAlt);
		}
		return true;
	}

	private static OpenTK.Mathematics.Vector3 BottomCenter(SceneObject o)
	{
		return o.Position + OpenTK.Mathematics.Vector3.Transform(new OpenTK.Mathematics.Vector3(0f, (0f - o.Size.Y) / 2f, 0f), o.Orientation);
	}

	private void Viewport_MouseMove(object sender, MouseEventArgs e)
	{
		if (_orbitHeld)
		{
			System.Windows.Point position = e.GetPosition(ViewportControl);
			float dx = (float)(position.X - _lastMouse.X);
			float dy = (float)(position.Y - _lastMouse.Y);
			_lastMouse = position;
			if (!CtrlHeld())
			{
				_scene.Look(dx, dy);
			}
			_needsFrame = true;
			return;
		}
		if (_marqueeActive)
		{
			System.Windows.Point position2 = e.GetPosition(ViewportControl);
			double value = position2.X - _marqueeStart.X;
			double value2 = position2.Y - _marqueeStart.Y;
			if (Math.Abs(value) > 4.0 || Math.Abs(value2) > 4.0)
			{
				MarqueeRect.Visibility = Visibility.Visible;
				MarqueeRect.Margin = new Thickness(Math.Min(position2.X, _marqueeStart.X), Math.Min(position2.Y, _marqueeStart.Y), 0.0, 0.0);
				MarqueeRect.Width = Math.Abs(value);
				MarqueeRect.Height = Math.Abs(value2);
			}
			return;
		}
		if (_objectDrag && _scene.Selected != null)
		{
			_dragMoved = true;
			System.Windows.Point position3 = e.GetPosition(ViewportControl);
			float w = (float)ViewportControl.ActualWidth;
			float h = (float)ViewportControl.ActualHeight;
			_scene.GetPickRay((float)position3.X, (float)position3.Y, w, h, out var origin, out var dir);
			bool flag;
			OpenTK.Mathematics.Vector3 point;
			OpenTK.Mathematics.Vector3 normal;
			if (AppSettings.Current.DragOnSurfaces)
			{
				flag = _scene.PickSurface((float)position3.X, (float)position3.Y, w, h, new HashSet<SceneObject>(DragTargets()), out point, out normal) || TryRayPlane(origin, dir, _scene.Selected.Position, _scene.Forward, out point);
			}
			else
			{
				normal = _scene.Forward;
				flag = TryRayPlane(origin, dir, _scene.Selected.Position, normal, out point);
			}
			if (!flag)
			{
				return;
			}
			SceneObject selected = _scene.Selected;
			OpenTK.Mathematics.Vector3 vector = selected.Size * 0.5f;
			float num = MathF.Abs(normal.X) * vector.X + MathF.Abs(normal.Y) * vector.Y + MathF.Abs(normal.Z) * vector.Z;
			OpenTK.Mathematics.Vector3 vector2 = point + normal * num + _objectDragOffset;
			OpenTK.Mathematics.Vector3 vector3 = ((SnapCheck.IsChecked == true) ? (_dragStartPos + SnapVec(vector2 - _dragStartPos)) : vector2) - selected.Position;
			HashSet<SceneObject> hashSet = (AppSettings.Current.DragCollision ? new HashSet<SceneObject>(DragTargets()) : null);
			foreach (SceneObject item in DragTargets())
			{
				item.Position = ((hashSet != null) ? ConstrainDragPosition(item, item.Position + vector3, hashSet) : (item.Position + vector3));
				if (_physics.Simulating)
				{
					_physics.Teleport(item);
				}
			}
			SyncGizmoPivot();
			UpdateLivePropsThrottled();
			_scene.InvalidateShadows();
			_needsFrame = true;
			return;
		}
		if (_dragAxis >= 0 && _scene.Selected != null)
		{
			_dragMoved = true;
			System.Windows.Point position4 = e.GetPosition(ViewportControl);
			float w2 = (float)ViewportControl.ActualWidth;
			float h2 = (float)ViewportControl.ActualHeight;
			_scene.GetPickRay((float)position4.X, (float)position4.Y, w2, h2, out var origin2, out var dir2);
			if (_scene.ActiveGizmo == GizmoKind.Rotate)
			{
				OpenTK.Mathematics.Vector3 vector4 = _scene.GizmoAxis(_dragAxis);
				float num2 = OpenTK.Mathematics.Vector3.Dot(dir2, vector4);
				if (MathF.Abs(num2) > 1E-06f)
				{
					float num3 = OpenTK.Mathematics.Vector3.Dot(_rotatePivot - origin2, vector4) / num2;
					if (num3 > 0f)
					{
						OpenTK.Mathematics.Vector3 vector5 = origin2 + dir2 * num3 - _rotatePivot;
						if (_dragR0.LengthSquared > 1E-10f && vector5.LengthSquared > 1E-10f)
						{
							_rotateAccum += MathF.Atan2(OpenTK.Mathematics.Vector3.Dot(OpenTK.Mathematics.Vector3.Cross(_dragR0, vector5), vector4), OpenTK.Mathematics.Vector3.Dot(_dragR0, vector5));
							float angle = ((SnapCheck.IsChecked == true) ? (MathF.Round(_rotateAccum * 180f / (float)Math.PI / 15f) * 15f * (float)Math.PI / 180f) : _rotateAccum);
							OpenTK.Mathematics.Quaternion quaternion = OpenTK.Mathematics.Quaternion.FromAxisAngle(vector4, angle);
							foreach (SceneObject item2 in DragTargets())
							{
								if (!item2.Locked && _dragPose.TryGetValue(item2, out (OpenTK.Mathematics.Vector3, OpenTK.Mathematics.Quaternion) value3))
								{
									item2.Position = _rotatePivot + OpenTK.Mathematics.Vector3.Transform(value3.Item1 - _rotatePivot, quaternion);
									OpenTK.Mathematics.Quaternion quaternion2 = quaternion * value3.Item2;
									System.Numerics.Quaternion quaternion3 = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(quaternion2.X, quaternion2.Y, quaternion2.Z, quaternion2.W));
									item2.Orientation = new OpenTK.Mathematics.Quaternion(quaternion3.X, quaternion3.Y, quaternion3.Z, quaternion3.W);
									item2.Rotation = SceneObject.QuatToEuler(item2.Orientation);
									if (_physics.Simulating)
									{
										_physics.Teleport(item2);
									}
								}
							}
							SyncGizmoPivot();
							_dragR0 = vector5;
						}
					}
				}
			}
			else if (_scene.ActiveGizmo == GizmoKind.Scale)
			{
				float num4 = OpenTK.Mathematics.Vector3.Dot(_scene.AxisPoint(_dragAxis, origin2, dir2) - _dragStartPos, _scene.GizmoAxis(_dragAxis));
				bool flag2 = CtrlHeld();
				bool flag3 = AltHeld();
				if ((flag2 != _scaleSymmetric || flag3 != _scaleUniform) && _scene.Selected != null)
				{
					_dragCoord0 = num4;
					_dragSize = _scene.Selected.Size;
					_dragSizes.Clear();
					foreach (SceneObject item3 in DragTargets())
					{
						_dragSizes[item3] = item3.Size;
					}
					_dragBottoms.Clear();
					foreach (SceneObject item4 in DragTargets())
					{
						_dragBottoms[item4] = BottomCenter(item4);
					}
					_scaleSymmetric = flag2;
					_scaleUniform = flag3;
				}
				_scaleRawS = SnapScaleCoordinate(num4, flag2 && !flag3);
				ApplyScaleSize(_scaleRawS, flag2, flag3);
			}
			else
			{
				OpenTK.Mathematics.Vector3 vector6 = _scene.AxisPoint(_dragAxis, origin2, dir2) + _dragOffset;
				OpenTK.Mathematics.Vector3 vector7 = vector6;
				if (SnapCheck.IsChecked == true)
				{
					OpenTK.Mathematics.Vector3 vector8 = _scene.GizmoAxis(_dragAxis);
					float num5 = SnapFloat(OpenTK.Mathematics.Vector3.Dot(vector6 - _dragStartPos, vector8));
					vector7 = _dragStartPos + vector8 * num5;
				}
				SceneObject selected2 = _scene.Selected;
				OpenTK.Mathematics.Vector3 vector9 = vector7 - selected2.Position;
				HashSet<SceneObject> hashSet2 = (AppSettings.Current.DragCollision ? new HashSet<SceneObject>(DragTargets()) : null);
				foreach (SceneObject item5 in DragTargets())
				{
					if (!item5.Locked)
					{
						item5.Position = ((hashSet2 != null) ? ConstrainDragPosition(item5, item5.Position + vector9, hashSet2) : (item5.Position + vector9));
						if (_physics.Simulating)
						{
							_physics.Teleport(item5);
						}
					}
				}
				SyncGizmoPivot();
			}
			UpdateLivePropsThrottled();
			_scene.InvalidateShadows();
			_needsFrame = true;
			return;
		}
		if (_lookHeld)
		{
			System.Windows.Point point2 = ViewportCenterScreen();
			if (GetCursorPos(out var lpPoint))
			{
				if (!CtrlHeld())
				{
					_scene.Look((float)lpPoint.X - (float)point2.X, (float)lpPoint.Y - (float)point2.Y);
				}
				SetCursorPos((int)point2.X, (int)point2.Y);
			}
			_needsFrame = true;
			return;
		}
		if (_panHeld)
		{
			System.Windows.Point position5 = e.GetPosition(ViewportControl);
			float dx2 = (float)(position5.X - _lastMouse.X);
			float dy2 = (float)(position5.Y - _lastMouse.Y);
			_lastMouse = position5;
			if (!CtrlHeld())
			{
				_scene.Pan(dx2, dy2);
			}
			_needsFrame = true;
			return;
		}
		if (_scene.Selected != null && _scene.ActiveGizmo != GizmoKind.None && ViewportControl.ActualWidth > 0.0)
		{
			System.Windows.Point position6 = e.GetPosition(ViewportControl);
			float w3 = (float)ViewportControl.ActualWidth;
			float h3 = (float)ViewportControl.ActualHeight;
			OpenTK.Mathematics.Vector3 ringPoint;
			int num6 = ((_scene.ActiveGizmo == GizmoKind.Rotate) ? _scene.PickRingAxis((float)position6.X, (float)position6.Y, w3, h3, FrameH(), out ringPoint) : _scene.PickGizmoAxis((float)position6.X, (float)position6.Y, w3, h3, FrameH()));
			if (num6 != _scene.HoverAxis)
			{
				_scene.HoverAxis = num6;
				_needsFrame = true;
			}
		}
		else if (_scene.HoverAxis != -1)
		{
			_scene.HoverAxis = -1;
			_needsFrame = true;
		}
		if (e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released && e.MiddleButton == MouseButtonState.Released && !_marqueeActive && _dragAxis < 0 && !_objectDrag && !_lookHeld && !_panHeld && !_orbitHeld && !_physics.Simulating && !_playerMode && ViewportControl.ActualWidth > 0.0)
		{
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - _lastHoverCheck).TotalMilliseconds >= 40.0)
			{
				_lastHoverCheck = utcNow;
				System.Windows.Point position7 = e.GetPosition(ViewportControl);
				SceneObject sceneObject = _scene.Pick((float)position7.X, (float)position7.Y, (float)ViewportControl.ActualWidth, (float)ViewportControl.ActualHeight, skipLocked: true);
				if (sceneObject != _scene.HoverObject)
				{
					_scene.HoverObject = sceneObject;
					_needsFrame = true;
				}
				var (sceneObject2, num7) = _scene.PickWaypoint((float)position7.X, (float)position7.Y, (float)ViewportControl.ActualWidth, (float)ViewportControl.ActualHeight);
				if (sceneObject2 != _scene.HoverWaypointObj || num7 != _scene.HoverWaypointIndex)
				{
					_scene.HoverWaypointObj = sceneObject2;
					_scene.HoverWaypointIndex = num7;
					_needsFrame = true;
				}
			}
		}
		else
		{
			if (_scene.HoverObject != null)
			{
				_scene.HoverObject = null;
				_needsFrame = true;
			}
			if (_scene.HoverWaypointObj != null)
			{
				_scene.HoverWaypointObj = null;
				_scene.HoverWaypointIndex = -1;
				_needsFrame = true;
			}
		}
	}

	private void UpdateLivePropsThrottled()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (!((utcNow - _lastPropPush).TotalMilliseconds < 33.0))
		{
			_lastPropPush = utcNow;
			UpdateLiveProps();
		}
	}

	private void FinishMarquee(System.Windows.Point mp)
	{
		_marqueeActive = false;
		bool num = MarqueeRect.Visibility == Visibility.Visible;
		MarqueeRect.Visibility = Visibility.Collapsed;
		if (!num)
		{
			return;
		}
		double num2 = Math.Min(_marqueeStart.X, mp.X);
		double num3 = Math.Max(_marqueeStart.X, mp.X);
		double num4 = Math.Min(_marqueeStart.Y, mp.Y);
		double num5 = Math.Max(_marqueeStart.Y, mp.Y);
		float num6 = (float)ViewportControl.ActualWidth;
		float num7 = (float)ViewportControl.ActualHeight;
		List<SceneObject> list = new List<SceneObject>();
		if (num6 > 0f && num7 > 0f)
		{
			foreach (SceneObject @object in _scene.Objects)
			{
				if (!@object.IsAvatarFace && _scene.WorldToScreen(@object.Position, num6, num7, out var sx, out var sy) && (double)sx >= num2 && (double)sx <= num3 && (double)sy >= num4 && (double)sy <= num5)
				{
					list.Add(@object);
				}
			}
		}
		if (!_marqueeAdditive)
		{
			_selection.Clear();
			_scene.Selected = null;
		}
		foreach (SceneObject item in list)
		{
			_selection.Add(item);
		}
		if (list.Count > 0)
		{
			_scene.Selected = list[list.Count - 1];
		}
		AfterSelectionChanged();
		if (_selection.Count > 1)
		{
			Log($"{_selection.Count} selected (marquee).");
		}
		if (num6 > 0f && num7 > 0f && !_physics.Simulating && !_playerMode)
		{
			double num8 = (num2 + num3) / 2.0;
			double num9 = (num4 + num5) / 2.0;
			SceneObject sceneObject = null;
			int num10 = -1;
			double num11 = double.MaxValue;
			foreach (SceneObject object2 in _scene.Objects)
			{
				if (!object2.IsMovingPlatform || object2.Hidden || object2.Waypoints.Count == 0)
				{
					continue;
				}
				for (int i = 0; i < object2.Waypoints.Count; i++)
				{
					if (_scene.WorldToScreen(object2.Waypoints[i], num6, num7, out var sx2, out var sy2) && !((double)sx2 < num2) && !((double)sx2 > num3) && !((double)sy2 < num4) && !((double)sy2 > num5))
					{
						double num12 = ((double)sx2 - num8) * ((double)sx2 - num8) + ((double)sy2 - num9) * ((double)sy2 - num9);
						if (num12 < num11)
						{
							num11 = num12;
							sceneObject = object2;
							num10 = i;
						}
					}
				}
			}
			_scene.SelWaypointObj = sceneObject;
			_scene.SelWaypointIndex = num10;
			SelectWaypointNode(sceneObject, num10);
			if (sceneObject != null)
			{
				StatusText.Text = $"Waypoint {num10 + 1} of {sceneObject.Name} selected";
			}
		}
		_needsFrame = true;
	}

	private void CancelMarquee()
	{
		_marqueeActive = false;
		MarqueeRect.Visibility = Visibility.Collapsed;
		if (!_lookHeld && !_panHeld && !_orbitHeld && _dragAxis < 0 && !_objectDrag)
		{
			ViewportControl.Cursor = null;
			if (ViewportControl.IsMouseCaptured)
			{
				ViewportControl.ReleaseMouseCapture();
			}
		}
	}

	private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Right)
		{
			_lookHeld = false;
		}
		if (e.ChangedButton == MouseButton.Middle)
		{
			_panHeld = false;
		}
		if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Right)
		{
			_orbitHeld = false;
		}
		if (e.ChangedButton == MouseButton.Left)
		{
			if (_marqueeActive)
			{
				FinishMarquee(e.GetPosition(ViewportControl));
			}
			bool flag = _dragAxis >= 0;
			bool objectDrag = _objectDrag;
			bool flag2 = flag && _scene.ActiveGizmo == GizmoKind.Scale;
			if (flag2 && _physics.Simulating && _scene.Selected != null)
			{
				ApplyScaleSize(_scaleRawS, _scaleSymmetric, _scaleUniform);
				UpdateLiveProps();
				foreach (SceneObject item in DragTargets())
				{
					_physics.RecreateBody(item);
				}
			}
			else if (flag2 && _scene.Selected != null)
			{
				ApplyScaleSize(_scaleRawS, _scaleSymmetric, _scaleUniform);
				UpdateLiveProps();
			}
			_dragAxis = -1;
			_objectDrag = false;
			_scene.ActiveAxis = -1;
			if ((flag || objectDrag) && _dragMoved && _dragBefore != null)
			{
				PushCaptured(_dragBefore, mergeable: false);
			}
			_dragBefore = null;
			_dragMoved = false;
		}
		if (!_lookHeld && !_panHeld && !_orbitHeld && _dragAxis < 0 && !_objectDrag)
		{
			ViewportControl.Cursor = null;
			if (ViewportControl.IsMouseCaptured)
			{
				ViewportControl.ReleaseMouseCapture();
			}
		}
	}

	private void CancelCameraDrag()
	{
		_lookHeld = false;
		_panHeld = false;
		_orbitHeld = false;
		_dragAxis = -1;
		_objectDrag = false;
		_dragBefore = null;
		_dragMoved = false;
		_scene.ActiveAxis = -1;
		ViewportControl.Cursor = null;
	}

	private System.Windows.Point ViewportCenterScreen()
	{
		return ViewportControl.PointToScreen(new System.Windows.Point(ViewportControl.ActualWidth / 2.0, ViewportControl.ActualHeight / 2.0));
	}

	private static bool TryRayPlane(OpenTK.Mathematics.Vector3 origin, OpenTK.Mathematics.Vector3 direction, OpenTK.Mathematics.Vector3 planePoint, OpenTK.Mathematics.Vector3 planeNormal, out OpenTK.Mathematics.Vector3 hit)
	{
		float num = OpenTK.Mathematics.Vector3.Dot(direction, planeNormal);
		if (MathF.Abs(num) < 1E-05f)
		{
			hit = OpenTK.Mathematics.Vector3.Zero;
			return false;
		}
		float num2 = OpenTK.Mathematics.Vector3.Dot(planePoint - origin, planeNormal) / num;
		if (num2 < 0f)
		{
			hit = OpenTK.Mathematics.Vector3.Zero;
			return false;
		}
		hit = origin + direction * num2;
		return true;
	}

	private OpenTK.Mathematics.Vector3 ConstrainDragPosition(SceneObject moving, OpenTK.Mathematics.Vector3 desired, HashSet<SceneObject>? ignore = null)
	{
		if (!moving.CanCollide || moving.IsAvatarFace)
		{
			return desired;
		}
		OpenTK.Mathematics.Vector3 half = moving.Size * 0.5f;
		OpenTK.Mathematics.Vector3 vector = new OpenTK.Mathematics.Vector3(MathF.Min(moving.Position.X, desired.X) - half.X, MathF.Min(moving.Position.Y, desired.Y) - half.Y, MathF.Min(moving.Position.Z, desired.Z) - half.Z);
		OpenTK.Mathematics.Vector3 vector2 = new OpenTK.Mathematics.Vector3(MathF.Max(moving.Position.X, desired.X) + half.X, MathF.Max(moving.Position.Y, desired.Y) + half.Y, MathF.Max(moving.Position.Z, desired.Z) + half.Z);
		List<SceneObject> list = new List<SceneObject>();
		foreach (SceneObject @object in _scene.Objects)
		{
			if (@object != moving && (ignore == null || !ignore.Contains(@object)) && !@object.IsAvatarFace && @object.CanCollide && @object.Shape != ShapeKind.None)
			{
				OpenTK.Mathematics.Vector3 vector3 = @object.Size * 0.5f;
				if (!(@object.Position.X + vector3.X < vector.X) && !(@object.Position.X - vector3.X > vector2.X) && !(@object.Position.Y + vector3.Y < vector.Y) && !(@object.Position.Y - vector3.Y > vector2.Y) && !(@object.Position.Z + vector3.Z < vector.Z) && !(@object.Position.Z - vector3.Z > vector2.Z))
				{
					list.Add(@object);
				}
			}
		}
		if (list.Count == 0)
		{
			return desired;
		}
		OpenTK.Mathematics.Vector3 vector4 = moving.Position;
		for (int i = 0; i < 2; i++)
		{
			vector4 = SweepTo(vector4, desired, half, list);
			if (!AnyOverlap(vector4, half, list))
			{
				break;
			}
		}
		if (AnyOverlap(vector4, half, list))
		{
			foreach (SceneObject item in list)
			{
				if (OverlapsSolid(vector4, half, item))
				{
					vector4 = SlideFace(moving.Position, desired, half, item, list);
					break;
				}
			}
		}
		return vector4;
	}

	private static bool AnyOverlap(OpenTK.Mathematics.Vector3 pos, OpenTK.Mathematics.Vector3 half, List<SceneObject> solids)
	{
		foreach (SceneObject solid in solids)
		{
			if (OverlapsSolid(pos, half, solid))
			{
				return true;
			}
		}
		return false;
	}

	private static OpenTK.Mathematics.Vector3 SweepTo(OpenTK.Mathematics.Vector3 pos, OpenTK.Mathematics.Vector3 goal, OpenTK.Mathematics.Vector3 half, List<SceneObject> solids)
	{
		for (int i = 0; i < 3; i++)
		{
			SetComponent(ref pos, i, SweepAxis(pos, half, solids, i, Component(goal, i) - Component(pos, i)));
		}
		return pos;
	}

	private static float SweepAxis(OpenTK.Mathematics.Vector3 pos, OpenTK.Mathematics.Vector3 half, List<SceneObject> solids, int axis, float delta)
	{
		if (delta == 0f)
		{
			return Component(pos, axis);
		}
		float num = Component(pos, axis);
		float num2 = num + delta;
		int axis2 = (axis + 1) % 3;
		int axis3 = (axis + 2) % 3;
		foreach (SceneObject solid in solids)
		{
			OpenTK.Mathematics.Vector3 v = solid.Size * 0.5f;
			if (!Overlaps1D(Component(pos, axis2), Component(half, axis2), Component(solid.Position, axis2), Component(v, axis2)) || !Overlaps1D(Component(pos, axis3), Component(half, axis3), Component(solid.Position, axis3), Component(v, axis3)))
			{
				continue;
			}
			if (delta > 0f)
			{
				float num3 = Component(solid.Position, axis) - Component(v, axis) - Component(half, axis);
				if (num3 >= num - 0.0001f && num3 < num2)
				{
					num2 = num3;
				}
			}
			else
			{
				float num4 = Component(solid.Position, axis) + Component(v, axis) + Component(half, axis);
				if (num4 <= num + 0.0001f && num4 > num2)
				{
					num2 = num4;
				}
			}
		}
		return num2;
	}

	private static bool OverlapsSolid(OpenTK.Mathematics.Vector3 pos, OpenTK.Mathematics.Vector3 half, SceneObject other)
	{
		OpenTK.Mathematics.Vector3 vector = other.Size * 0.5f;
		if (Overlaps1D(pos.X, half.X, other.Position.X, vector.X) && Overlaps1D(pos.Y, half.Y, other.Position.Y, vector.Y))
		{
			return Overlaps1D(pos.Z, half.Z, other.Position.Z, vector.Z);
		}
		return false;
	}

	private static bool Overlaps1D(float a, float ha, float b, float hb)
	{
		return MathF.Abs(a - b) < ha + hb - 0.0001f;
	}

	private static OpenTK.Mathematics.Vector3 SlideFace(OpenTK.Mathematics.Vector3 start, OpenTK.Mathematics.Vector3 desired, OpenTK.Mathematics.Vector3 half, SceneObject other, List<SceneObject> solids)
	{
		OpenTK.Mathematics.Vector3 vector = other.Size * 0.5f;
		OpenTK.Mathematics.Vector3 vector2 = other.Position - vector - half;
		OpenTK.Mathematics.Vector3 vector3 = other.Position + vector + half;
		OpenTK.Mathematics.Vector3 result = start;
		float num = float.MaxValue;
		OpenTK.Mathematics.Vector3 result2 = start;
		float num2 = float.MaxValue;
		OpenTK.Mathematics.Vector3[] array = new OpenTK.Mathematics.Vector3[6]
		{
			new OpenTK.Mathematics.Vector3(vector2.X, desired.Y, desired.Z),
			new OpenTK.Mathematics.Vector3(vector3.X, desired.Y, desired.Z),
			new OpenTK.Mathematics.Vector3(desired.X, vector2.Y, desired.Z),
			new OpenTK.Mathematics.Vector3(desired.X, vector3.Y, desired.Z),
			new OpenTK.Mathematics.Vector3(desired.X, desired.Y, vector2.Z),
			new OpenTK.Mathematics.Vector3(desired.X, desired.Y, vector3.Z)
		};
		foreach (OpenTK.Mathematics.Vector3 vector4 in array)
		{
			float lengthSquared = (vector4 - desired).LengthSquared;
			bool num3 = !OverlapsSolid(vector4, half, other);
			if (num3 && lengthSquared < num2)
			{
				num2 = lengthSquared;
				result2 = vector4;
			}
			if (!num3)
			{
				continue;
			}
			bool flag = true;
			foreach (SceneObject solid in solids)
			{
				if (OverlapsSolid(vector4, half, solid))
				{
					flag = false;
					break;
				}
			}
			if (flag && lengthSquared < num)
			{
				num = lengthSquared;
				result = vector4;
			}
		}
		if (num < float.MaxValue)
		{
			return result;
		}
		if (num2 < float.MaxValue)
		{
			return result2;
		}
		return start;
	}

	private void Viewport_Wheel(object sender, MouseWheelEventArgs e)
	{
		if (!CtrlHeld())
		{
			if (_physics.Simulating && _scene.FollowTarget != null)
			{
				float zoomSpeed = AppSettings.Current.ZoomSpeed;
				float num = Math.Clamp(_scene.FollowDistance, 2f, 120f);
				_scene.FollowDistance = Math.Clamp(num - (float)Math.Sign(e.Delta) * Math.Clamp(num * 0.15f, 0.3f, 9f) * zoomSpeed, 2f, 120f);
			}
			else
			{
				float num2 = Math.Clamp(_scene.Position.Length, 1.5f, 200f);
				_scene.Dolly((float)((e.Delta > 0) ? 1 : (-1)) * Math.Clamp(num2 * 0.12f, 0.3f, 4f) * AppSettings.Current.ZoomSpeed);
			}
			_needsFrame = true;
		}
	}

	private void Viewport_KeyDown(object sender, KeyEventArgs e)
	{
		if (_playerMode)
		{
			return;
		}
		bool flag = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
		if (e.Key == Key.Delete && !e.IsRepeat)
		{
			Delete_Click(sender, e);
			e.Handled = true;
			return;
		}
		if (flag && e.Key == Key.D && !e.IsRepeat)
		{
			Duplicate_Click(sender, e);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.F5)
		{
			if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
			{
				StopPlaying();
			}
			else
			{
				StartPlaying();
			}
			e.Handled = true;
			return;
		}
		if (e.Key == Key.F)
		{
			SceneObject selected = _scene.Selected;
			if (selected != null)
			{
				_scene.FocusOn(selected.Position, selected.Size.Length / 2f);
				Log("Focused " + selected.Name + " (F).");
			}
			else
			{
				_scene.ResetCamera();
				Log("Camera reset to origin (F, nothing selected).");
			}
			_needsFrame = true;
			return;
		}
		bool flag2;
		switch (e.Key)
		{
		case Key.A:
		case Key.D:
		case Key.E:
		case Key.Q:
		case Key.S:
		case Key.W:
		case Key.LeftShift:
		case Key.RightShift:
			flag2 = true;
			break;
		default:
			flag2 = false;
			break;
		}
		if (flag2)
		{
			_needsFrame = true;
		}
	}

	private void RibbonButton_Click(object sender, RoutedEventArgs e)
	{
		string text = ((sender is Fluent.Button button) ? (button.Header?.ToString() ?? "Button") : ((sender is Fluent.ToggleButton toggleButton) ? (toggleButton.Header?.ToString() ?? "Toggle") : ((!(sender is System.Windows.Controls.Button { Content: var content })) ? (sender?.GetType().Name ?? "?") : (content?.ToString() ?? "Button"))));
		string text2 = text;
		Log("Ribbon: " + text2);
		StatusText.Text = text2;
	}

	private void Tool_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Fluent.ToggleButton toggleButton)
		{
			SetTool(toggleButton.Header?.ToString()?.Split(' ').Last() ?? "Select");
			Log("Tool ? " + _tool);
			StatusText.Text = "Tool: " + _tool;
		}
	}

	private void SetTool(string tool)
	{
		_tool = tool;
		ToolLabel.Text = "Tool: " + _tool;
		StudioScene scene = _scene;
		scene.ActiveGizmo = _tool switch
		{
			"Move" => GizmoKind.Move, 
			"Scale" => GizmoKind.Scale, 
			"Rotate" => GizmoKind.Rotate, 
			_ => GizmoKind.None, 
		};
		MoveToolToggle.IsChecked = _tool == "Move";
	}

	private void Anchor_Click(object sender, RoutedEventArgs e)
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			return;
		}
		PushUndo("Anchored", mergeable: false);
		bool anchored = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		foreach (SceneObject item in DragTargets())
		{
			item.Anchored = anchored;
		}
		if (_physics.Simulating)
		{
			foreach (SceneObject item2 in DragTargets())
			{
				_physics.RecreateBody(item2);
			}
		}
		MarkDirty();
		SyncAnchorToggle();
		UpdateLiveProps();
		Log($"? {selected.Name} {(selected.Anchored ? "anchored" : "unanchored")}.");
		StatusText.Text = selected.Name + " " + (selected.Anchored ? "anchored" : "unanchored");
	}

	private void SyncAnchorToggle()
	{
		SceneObject selected = _scene.Selected;
		if (selected != null)
		{
			AnchorToggle.IsEnabled = true;
			AnchorToggle.IsChecked = selected.Anchored;
			AnchorToggleHome.IsEnabled = true;
			AnchorToggleHome.IsChecked = selected.Anchored;
		}
		else
		{
			AnchorToggle.IsChecked = false;
			AnchorToggle.IsEnabled = false;
			AnchorToggleHome.IsChecked = false;
			AnchorToggleHome.IsEnabled = false;
		}
		SyncInspectorHeader();
	}

	private void Velocity_Click(object sender, RoutedEventArgs e)
	{
		List<SceneObject> list = (from o in DragTargets()
			where o.Shape != ShapeKind.None || o.Light == LightKind.None
			select o).ToList();
		if (list.Count == 0 || _scene.Selected == null)
		{
			Log("Velocity: select a part first.");
			StatusText.Text = "Select a part first";
			return;
		}
		PushUndo("Velocity", mergeable: false);
		bool flag = list.All((SceneObject o) => o.HasVelocity);
		foreach (SceneObject item in list)
		{
			if (flag)
			{
				item.VelocityDirection = OpenTK.Mathematics.Vector3.Zero;
				item.VelocitySpeed = 0f;
				item.VelocityMode = SpeedMode.Stable;
				continue;
			}
			if (item.VelocityDirection.LengthSquared < 1E-08f)
			{
				item.VelocityDirection = new OpenTK.Mathematics.Vector3(0f, 0f, 1f);
			}
			if (item.VelocitySpeed <= 0.01f)
			{
				item.VelocitySpeed = 5f;
			}
			item.VelocityMode = SpeedMode.Accelerating;
		}
		if (_scene.Selected != null)
		{
			_liveRows = ObjectProperties(_scene.Selected);
			SetPropertyRows(_liveRows);
			MarkVaryingRows();
		}
		MarkDirty();
		_needsFrame = true;
		if (flag)
		{
			Log($"Velocity removed from {list.Count} part(s).");
			StatusText.Text = "Velocity removed";
			return;
		}
		Log($"Velocity ? 5 studs/s Accelerating on {list.Count} part(s).");
		if (list.Any((SceneObject o) => o.Anchored))
		{
			Log("Note: velocity drives unanchored parts; anchored parts stay put.");
		}
		StatusText.Text = $"Velocity on {list.Count} part(s)";
	}

	/// <summary>Internal clipboard: frozen copies (copy-time clones, so later
	/// edits don't mutate the buffer and pasting twice works).</summary>
	private readonly List<SceneObject> _clipboard = new List<SceneObject>();

	private void Duplicate_Click(object sender, RoutedEventArgs e)	{
		List<SceneObject> list = DragTargets().ToList();
		if (list.Count == 0)
		{
			Log("Duplicate: nothing selected.");
			StatusText.Text = "Nothing to duplicate";
			return;
		}
		PushUndo("Duplicate", mergeable: false);
		List<SceneObject> list2 = new List<SceneObject>();
		foreach (SceneObject item in list)
		{
			SceneObject sceneObject = item.Clone(item.Name);
			_scene.Objects.Add(sceneObject);
			_physics.AddBody(sceneObject);
			list2.Add(sceneObject);
		}
		_scene.InvalidateShadows();
		AppendExplorerNodes(list2);
		_selection.Clear();
		_selection.UnionWith(list2);
		_scene.Selected = list2[list2.Count - 1];
		AfterSelectionChanged();
		if (_tool == "Select")
		{
			SetTool("Move");
		}
		Log($"Duplicated {list2.Count} part{((list2.Count == 1) ? "" : "s")}.");
		StatusText.Text = $"Duplicated {list2.Count} part{((list2.Count == 1) ? "" : "s")}";
		MarkDirty();
	}

	/// <summary>Copy selection to the clipboard (frozen clones, positions kept).</summary>
	private void Copy_Click(object sender, RoutedEventArgs e)
	{
		List<SceneObject> list = DragTargets().ToList();
		if (list.Count == 0)
		{
			Log("Copy: nothing selected.");
			StatusText.Text = "Nothing to copy";
			return;
		}
		_clipboard.Clear();
		foreach (SceneObject item in list)
		{
			_clipboard.Add(item.Clone(item.Name));
		}
		Log($"Copied {list.Count} part{((list.Count == 1) ? "" : "s")}.");
		StatusText.Text = $"Copied {list.Count} part{((list.Count == 1) ? "" : "s")}";
	}

	/// <summary>Cut = copy, then the guarded delete path (locked-safe).</summary>
	private void Cut_Click(object sender, RoutedEventArgs e)
	{
		if (DragTargets().ToList().Count == 0)
		{
			Log("Cut: nothing selected.");
			StatusText.Text = "Nothing to cut";
			return;
		}
		Copy_Click(sender, e);
		Delete_Click(sender, e);
	}

	/// <summary>Paste the clipboard exactly in place (no offset, unlocked copies).</summary>
	private void Paste_Click(object sender, RoutedEventArgs e)
	{
		if (_clipboard.Count == 0)
		{
			Log("Paste: clipboard is empty (copy something first).");
			StatusText.Text = "Clipboard is empty";
			return;
		}
		PushUndo("Paste", mergeable: false);
		List<SceneObject> list2 = new List<SceneObject>();
		foreach (SceneObject item in _clipboard)
		{
			SceneObject sceneObject = item.Clone(item.Name);
			sceneObject.Locked = false;
			_scene.Objects.Add(sceneObject);
			_physics.AddBody(sceneObject);
			list2.Add(sceneObject);
		}
		_scene.InvalidateShadows();
		AppendExplorerNodes(list2);
		_selection.Clear();
		_selection.UnionWith(list2);
		_scene.Selected = list2[list2.Count - 1];
		AfterSelectionChanged();
		if (_tool == "Select")
		{
			SetTool("Move");
		}
		Log($"Pasted {list2.Count} part{((list2.Count == 1) ? "" : "s")} in place.");
		StatusText.Text = $"Pasted {list2.Count} part{((list2.Count == 1) ? "" : "s")}";
		MarkDirty();
	}

	private void RotateSelected90()
	{
		List<SceneObject> list = DragTargets().ToList();
		if (list.Count == 0)
		{
			StatusText.Text = "Nothing to rotate";
			return;
		}
		PushUndo("Rotate 90\u00B0", mergeable: false);
		OpenTK.Mathematics.Quaternion quaternion = OpenTK.Mathematics.Quaternion.FromAxisAngle(OpenTK.Mathematics.Vector3.UnitY, (float)Math.PI / 2f);
		foreach (SceneObject item in list)
		{
			OpenTK.Mathematics.Quaternion quaternion2 = quaternion * item.Orientation;
			System.Numerics.Quaternion quaternion3 = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(quaternion2.X, quaternion2.Y, quaternion2.Z, quaternion2.W));
			item.Orientation = new OpenTK.Mathematics.Quaternion(quaternion3.X, quaternion3.Y, quaternion3.Z, quaternion3.W);
			item.Rotation = SceneObject.QuatToEuler(item.Orientation);
		}
		AfterSelectionChanged();
		_scene.InvalidateShadows();
		MarkDirty();
		_needsFrame = true;
		Log($"Rotated {list.Count} part{((list.Count == 1) ? "" : "s")} 90\u00B0 (R).");
		StatusText.Text = "Rotated 90\u00B0";
	}

	private void Delete_Click(object sender, RoutedEventArgs e)
	{
		List<SceneObject> list = DragTargets().ToList();
		int num = list.RemoveAll((SceneObject o) => o.Locked);
		if (list.Count == 0)
		{
			Log((num > 0) ? "Delete: selection is locked." : "Delete: nothing selected.");
			StatusText.Text = ((num > 0) ? "Locked parts can't be deleted" : "Nothing to delete");
			return;
		}
		int val = _scene.Objects.IndexOf(_scene.Selected);
		PushUndo("Delete", mergeable: false);
		foreach (SceneObject item in list)
		{
			_physics.RemoveBody(item);
			if (item == _physics.Player)
			{
				SceneObject faceObject = _physics.FaceObject;
				if (faceObject != null)
				{
					_scene.Objects.Remove(faceObject);
				}
				_physics.ClearPlayer();
				_scene.FollowTarget = null;
			}
			_scene.Objects.Remove(item);
		}
		_scene.InvalidateShadows();
		MarkDirty();
		RemoveExplorerNodes(list);
		SelectObject((_scene.Objects.Count > 0) ? _scene.Objects[Math.Min(Math.Max(val, 0), _scene.Objects.Count - 1)] : null);
		Log($"Deleted {list.Count} part{((list.Count == 1) ? "" : "s")}.");
		StatusText.Text = $"Deleted {list.Count} part{((list.Count == 1) ? "" : "s")}";
	}

	private void InsertPart_Click(object sender, RoutedEventArgs e)
	{
		InsertShape(ShapeKind.Block, "Part");
	}

	private void InsertWedge_Click(object sender, RoutedEventArgs e)
	{
		InsertShape(ShapeKind.Wedge, "Wedge");
	}

	private void InsertKillbrick_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Killbrick {_partCounter}",
			Shape = ShapeKind.Killbrick,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(2f, 2f, 2f),
			Color = new Color4(1f, 0.15f, 0.15f, 1f),
			Anchored = true,
			Damage = 100f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		Log("Inserted " + sceneObject.Name + " (killbrick, Damage 100) in front of camera.");
		StatusText.Text = "Inserted " + sceneObject.Name;
		MarkDirty();
	}

	private void InsertNpc_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert NPC", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"NPC {_partCounter}",
			Shape = ShapeKind.Capsule,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(1.5f, 1.5f, 1.5f),
			Color = new Color4(0.95f, 0.45f, 0.2f, 1f),
			IsNpc = true,
			NpcType = NpcKind.Enemy,
			NpcDamage = 15f,
			NpcSpeed = 3f,
			NpcDialogue = "Grr!|Come here!|You can't run!",
			NpcChatRange = 10f,
			NpcChatInterval = 5f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (Enemy NPC). Configure Type, Damage, Speed, Dialogue and Chat in Properties.");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertTimedPart_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Timed Part", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Timed Part {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(4f, 0.5f, 4f),
			Color = new Color4(0.2f, 0.85f, 0.85f, 1f),
			Material = MaterialKind.Neon,
			Anchored = true,
			IsTimedPart = true,
			TimedHidden = 1f,
			TimedOffset = 0f,
			TimedVisible = 1f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (1s visible / 1s hidden loop). Tune Hidden time, Timed offset, Visible time in Properties.");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertCheckpoint_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Checkpoint", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Checkpoint {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(3f, 0.4f, 3f),
			Color = new Color4(1f, 0.85f, 0.2f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Anchored = true,
			IsCheckpoint = true
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (touch to respawn here).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertMesh_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Import mesh",
			Filter = "3D models|*.fbx;*.obj|All files|*.*",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog(this) == true)
		{
			PushUndo("Insert Mesh", mergeable: false);
			_partCounter++;
			OpenTK.Mathematics.Vector3 position = InsertPoint();
			SceneObject sceneObject = new SceneObject
			{
				Name = $"Mesh {_partCounter}",
				Shape = ShapeKind.Mesh,
				MeshPath = openFileDialog.FileName,
				Position = position,
				Size = new OpenTK.Mathematics.Vector3(1f, 1f, 1f),
				Color = new Color4(1f, 1f, 1f, 1f),
				Material = MaterialKind.Plastic,
				Anchored = true
			};
			_scene.Objects.Add(sceneObject);
			_physics.AddBody(sceneObject);
			_scene.InvalidateShadows();
			SelectObject(sceneObject);
			MarkDirty();
			Log($"Inserted {sceneObject.Name} from {openFileDialog.FileName} (renders the file, collides as a box).");
			StatusText.Text = "Inserted " + sceneObject.Name;
		}
	}

	private void InsertBouncePad_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Bounce Pad", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Bounce Pad {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(3f, 0.4f, 3f),
			Color = new Color4(1f, 0.5f, 0.1f, 1f),
			Material = MaterialKind.Neon,
			Anchored = true,
			IsBouncePad = true,
			BouncePower = 20f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (launches 20 studs/s; tune Bounce power in Properties).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertMagnet_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Magnet", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Magnet {_partCounter}",
			Shape = ShapeKind.Ball,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(2f, 2f, 2f),
			Color = new Color4(1f, 0.2f, 0.2f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Anchored = true,
			IsMagnet = true,
			MagnetPower = 15f,
			MagnetRange = 10f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (pulls things in Play mode; tune Magnet power/range).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertConveyor_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Conveyor", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Conveyor {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(6f, 0.5f, 6f),
			Color = new Color4(0.6f, 0.6f, 0.65f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Anchored = true,
			IsConveyor = true,
			ConveyorDirection = OpenTK.Mathematics.Vector3.UnitX,
			ConveyorSpeed = 8f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (rides things in Play mode; tune Direction/speed).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertMovingPlatform_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Moving Platform", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Moving Platform {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(4f, 0.5f, 4f),
			Color = new Color4(0.9f, 0.6f, 0.2f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Anchored = true,
			IsMovingPlatform = true,
			MoveDistance = 8f,
			MoveSpeed = 2f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (slides in Play mode; tune distance/speed).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertSpinner_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Spinner", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Spinner {_partCounter}",
			Shape = ShapeKind.Cylinder,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(6f, 0.5f, 6f),
			Color = new Color4(0.6f, 0.4f, 0.9f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Anchored = true,
			IsSpinner = true,
			SpinSpeed = 45f
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (spins in Play mode; tune Spin speed).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertBreakable_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Breakable", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Breakable {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(4f, 4f, 4f),
			Color = new Color4(0.65f, 0.45f, 0.25f, 1f),
			Material = MaterialKind.Wood,
			Anchored = false,
			IsBreakable = true,
			BreakHits = 2
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (stomp twice to shatter in Play mode).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertScriptBlock_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Script", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Script {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(1f, 1f, 1f),
			Color = new Color4(0.35f, 0.75f, 1f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Anchored = true
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		ScriptEditorWindow scriptEditorWindow = new ScriptEditorWindow("", sceneObject.Name)
		{
			Owner = this
		};
		if (scriptEditorWindow.ShowDialog() == true && scriptEditorWindow.Code != "")
		{
			sceneObject.Script = scriptEditorWindow.Code;
			_liveRows = ObjectProperties(sceneObject);
			SetPropertyRows(_liveRows);
			MarkDirty();
			Log("Script saved on " + sceneObject.Name + ".");
			StatusText.Text = "Script saved on " + sceneObject.Name;
		}
		else
		{
			Log("Inserted " + sceneObject.Name + " (empty script: Properties > Script to add code).");
			StatusText.Text = "Inserted " + sceneObject.Name;
		}
	}

	private void InsertWater_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert Water", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Water {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(8f, 3f, 8f),
			Color = new Color4(0.15f, 0.4f, 0.9f, 1f),
			Material = MaterialKind.SmoothPlastic,
			Transparency = 0.45f,
			Anchored = true,
			CanCollide = false,
			IsWater = true
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log("Inserted " + sceneObject.Name + " (swim with WASD, Space swims up).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void InsertLight_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert PointLight", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"PointLight {_partCounter}",
			Shape = ShapeKind.None,
			Light = LightKind.Point,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(1f, 1f, 1f),
			Color = new Color4(1f, 1f, 1f, 1f),
			Brightness = 2f,
			Range = 16f,
			Anchored = true
		};
		_scene.Objects.Add(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		Log("Inserted " + sceneObject.Name + " in front of camera.");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private OpenTK.Mathematics.Vector3 InsertPoint()
	{
		OpenTK.Mathematics.Vector3? toolboxDropPoint = _toolboxDropPoint;
		if (toolboxDropPoint.HasValue)
		{
			OpenTK.Mathematics.Vector3 valueOrDefault = toolboxDropPoint.GetValueOrDefault();
			_toolboxDropPoint = null;
			return valueOrDefault;
		}
		OpenTK.Mathematics.Vector3 vector = _scene.Position + _scene.Forward * 6f;
		if (SnapCheck.IsChecked == true)
		{
			vector = SnapVec(vector);
		}
		return vector;
	}

	private OpenTK.Mathematics.Vector3 SnapDropPoint(OpenTK.Mathematics.Vector3 p, float lift)
	{
		if (SnapCheck.IsChecked == true)
		{
			OpenTK.Mathematics.Vector3 vector = SnapVec(new OpenTK.Mathematics.Vector3(p.X, 0f, p.Z));
			return new OpenTK.Mathematics.Vector3(vector.X, p.Y + lift, vector.Z);
		}
		return p + new OpenTK.Mathematics.Vector3(0f, lift, 0f);
	}

	private void InsertShape_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ShapeKind>(tag, out var result))
		{
			InsertShape(result, result.ToString());
		}
	}

	private SceneObject? LoadSpawnMesh(OpenTK.Mathematics.Vector3 at, string name)
	{
		try
		{
			string path = System.IO.Path.Combine(ModelsFolder, "Spawn.bp");
			if (!File.Exists(path) || !TryReadPlaceFile(path, out PlaceFile file) || file == null)
			{
				return null;
			}
			PartDto partDto = (file.Parts ?? new List<PartDto>()).FirstOrDefault();
			if (partDto == null)
			{
				return null;
			}
			string directoryName = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
			SceneObject sceneObject = DtoToPart(partDto, directoryName);
			sceneObject.Name = name;
			sceneObject.Position = at;
			sceneObject.IsSpawn = true;
			return sceneObject;
		}
		catch
		{
			return null;
		}
	}

	private void EnsureSpawn(OpenTK.Mathematics.Vector3 at)
	{
		if (!_scene.Objects.Any((SceneObject o) => o.IsSpawn))
		{
			SceneObject sceneObject = LoadSpawnMesh(at, "Spawn");
			if (sceneObject == null)
			{
				sceneObject = new SceneObject
				{
					Name = "Spawn",
					Shape = ShapeKind.Block,
					Position = at,
					Size = new OpenTK.Mathematics.Vector3(2f, 0.5f, 2f),
					Color = new Color4(0.75f, 0.75f, 0.78f, 1f),
					Anchored = true,
					IsSpawn = true
				};
			}
			_scene.Objects.Add(sceneObject);
			_physics.AddBody(sceneObject);
		}
	}

	private void InsertSpawn_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 vector = InsertPoint();
		SceneObject sceneObject = LoadSpawnMesh(vector, $"Spawn {_partCounter}");
		if (sceneObject == null)
		{
			sceneObject = new SceneObject
			{
				Name = $"Spawn {_partCounter}",
				Shape = ShapeKind.Block,
				Position = vector,
				Size = new OpenTK.Mathematics.Vector3(2f, 0.5f, 2f),
				Color = new Color4(0.75f, 0.75f, 0.78f, 1f),
				Anchored = true,
				IsSpawn = true
			};
		}
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		Log("Inserted " + sceneObject.Name + " (spawn point) in front of camera.");
		StatusText.Text = "Inserted " + sceneObject.Name;
		MarkDirty();
	}

	private void InsertTeleportPads_Click(object sender, RoutedEventArgs e)
	{
		PushUndo("Insert", mergeable: false);
		_teleportCounter++;
		string text = $"Pad{_teleportCounter}";
		OpenTK.Mathematics.Vector3 vector = InsertPoint();
		OpenTK.Mathematics.Vector3 vector2 = OpenTK.Mathematics.Vector3.Normalize(OpenTK.Mathematics.Vector3.Cross(_scene.Forward, OpenTK.Mathematics.Vector3.UnitY));
		if (vector2.LengthSquared < 1E-06f)
		{
			vector2 = OpenTK.Mathematics.Vector3.UnitX;
		}
		OpenTK.Mathematics.Vector3 vector3 = vector - vector2 * 3f;
		OpenTK.Mathematics.Vector3 vector4 = vector + vector2 * 3f;
		if (SnapCheck.IsChecked == true)
		{
			vector3 = SnapVec(vector3);
			vector4 = SnapVec(vector4);
		}
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Teleport A{_teleportCounter}",
			Shape = ShapeKind.Block,
			Position = vector3,
			Size = new OpenTK.Mathematics.Vector3(2f, 0.5f, 2f),
			Color = new Color4(0.2f, 0.5f, 1f, 1f),
			Anchored = true,
			TeleportLink = text
		};
		SceneObject sceneObject2 = new SceneObject
		{
			Name = $"Teleport B{_teleportCounter}",
			Shape = ShapeKind.Block,
			Position = vector4,
			Size = new OpenTK.Mathematics.Vector3(2f, 0.5f, 2f),
			Color = new Color4(1f, 0.55f, 0.15f, 1f),
			Anchored = true,
			TeleportLink = text
		};
		_partCounter += 2;
		_scene.Objects.Add(sceneObject);
		_scene.Objects.Add(sceneObject2);
		_physics.AddBody(sceneObject);
		_physics.AddBody(sceneObject2);
		_scene.InvalidateShadows();
		SelectObject(sceneObject2);
		Log($"Inserted Teleport A{_teleportCounter} + B{_teleportCounter} (link {text}) in front of camera.");
		StatusText.Text = "Inserted teleport pads (" + text + ")";
		MarkDirty();
	}

	private static string FaceFromNormal(SceneObject o, OpenTK.Mathematics.Vector3 worldNormal)
	{
		OpenTK.Mathematics.Quaternion quat = OpenTK.Mathematics.Quaternion.Invert(o.Orientation);
		OpenTK.Mathematics.Vector3 vector = OpenTK.Mathematics.Vector3.Transform(worldNormal, quat);
		float num = Math.Abs(vector.X);
		float num2 = Math.Abs(vector.Y);
		float num3 = Math.Abs(vector.Z);
		if (num >= num2 && num >= num3)
		{
			if (!(vector.X > 0f))
			{
				return "Left";
			}
			return "Right";
		}
		if (num2 >= num && num2 >= num3)
		{
			if (!(vector.Y > 0f))
			{
				return "Bottom";
			}
			return "Top";
		}
		if (!(vector.Z > 0f))
		{
			return "Back";
		}
		return "Front";
	}

	public void SpawnToolboxItem(string id, OpenTK.Mathematics.Vector3? drop, SceneObject? onPart, string face = "Front")
	{
		RoutedEventArgs e = new RoutedEventArgs();
		float lift = DropLift(id);
		if (drop.HasValue)
		{
			OpenTK.Mathematics.Vector3 valueOrDefault = drop.GetValueOrDefault();
			_toolboxDropPoint = SnapDropPoint(valueOrDefault, lift);
		}
		if (id.StartsWith("shape:"))
		{
			if (!Enum.TryParse<ShapeKind>(id.Substring(6), out var result))
			{
				_toolboxDropPoint = null;
				Log("Toolbox: unknown item " + id);
				return;
			}
			InsertShape(result, result.ToString());
		}
		else if (id.StartsWith("img:"))
		{
			_toolboxDropPoint = null;
			ApplyToolboxImage(id.Substring(4), onPart, face);
		}
		else if (id.StartsWith("snd:"))
		{
			_toolboxDropPoint = null;
			ApplyToolboxSound(id.Substring(4), onPart);
		}
		else if (id.StartsWith("model:"))
		{
			SpawnToolboxModel(id.Substring(6), drop);
		}
		else
		{
			switch (id)
			{
			case "plane":
				InsertToolboxPlane();
				break;
			case "mesh":
				InsertToolboxMesh();
				break;
			case "scriptblock":
				InsertScriptBlock_Click(this, e);
				break;
			case "light":
				InsertLight_Click(this, e);
				break;
			case "spawn":
				InsertSpawn_Click(this, e);
				break;
			case "kill":
				InsertKillbrick_Click(this, e);
				break;
			case "npc":
				InsertNpc_Click(this, e);
				break;
			case "checkpoint":
				InsertCheckpoint_Click(this, e);
				break;
			case "bounce":
				InsertBouncePad_Click(this, e);
				break;
			case "magnet":
				InsertMagnet_Click(this, e);
				break;
			case "conveyor":
				InsertConveyor_Click(this, e);
				break;
			case "mover":
				InsertMovingPlatform_Click(this, e);
				break;
			case "spinner":
				InsertSpinner_Click(this, e);
				break;
			case "breakable":
				InsertBreakable_Click(this, e);
				break;
			case "water":
				InsertWater_Click(this, e);
				break;
			case "teleport":
				InsertTeleportPads_Click(this, e);
				break;
			case "timed":
				InsertTimedPart_Click(this, e);
				break;
			case "decal":
			case "text":
			{
				SceneObject sceneObject = onPart ?? _scene.Selected;
				if (sceneObject == null || sceneObject.Shape == ShapeKind.None)
				{
					_toolboxDropPoint = null;
					Log(((id == "decal") ? "Decal" : "Text") + ": drop onto a part (or select one first).");
					StatusText.Text = "Drop onto a part";
					return;
				}
				SelectObject(sceneObject);
				_toolboxDropPoint = null;
				if (id == "decal")
				{
					ApplyDecalDialog(sceneObject, face);
				}
				else
				{
					ApplyText_Click(this, e);
				}
				break;
			}
			default:
				_toolboxDropPoint = null;
				Log("Toolbox: unknown item " + id);
				return;
			}
		}
		SetTool("Move");
		_needsFrame = true;
	}

	public void SaveSelectedAsModel()
	{
		List<SceneObject> list = (from o in DragTargets()
			where o != _physics.Player && !o.IsAvatarFace
			select o).ToList();
		if (list.Count == 0)
		{
			Log("Save model: select one or more parts first.");
			StatusText.Text = "Select parts first";
			return;
		}
		Directory.CreateDirectory(ModelsFolder);
		string text = SafeGameName((list.Count == 1) ? list[0].Name : "Model");
		string text2 = text;
		string path = System.IO.Path.Combine(ModelsFolder, text2 + ".bp");
		int num = 2;
		while (File.Exists(path))
		{
			text2 = $"{text} {num}";
			path = System.IO.Path.Combine(ModelsFolder, text2 + ".bp");
			num++;
		}
		PlaceFile placeFile = new PlaceFile
		{
			Parts = (from t in list
				where t.Shape != ShapeKind.None || t.Light == LightKind.None
				select CapturePart(t, null)).ToList(),
			Lights = list.Where((SceneObject t) => t.Shape == ShapeKind.None && t.Light != LightKind.None).Select(CaptureLight).ToList()
		};
		try
		{
			File.WriteAllText(path, JsonSerializer.Serialize(placeFile, PlaceJson));
			Log($"Model saved: {text2} ({placeFile.Parts.Count + placeFile.Lights.Count} objects).");
			StatusText.Text = "Saved " + text2;
			ToolboxTabs.RefreshModels();
		}
		catch (Exception ex)
		{
			Log("Save model failed: " + ex.Message);
			StatusText.Text = "Save failed";
		}
	}

	public void DeleteToolboxModel(string path)
	{
		try
		{
			File.Delete(path);
			Log("Model deleted: " + System.IO.Path.GetFileNameWithoutExtension(path));
			StatusText.Text = "Model deleted";
			ToolboxTabs.RefreshModels();
		}
		catch (Exception ex)
		{
			Log("Delete model failed: " + ex.Message);
			StatusText.Text = "Delete failed";
		}
	}

	private void SpawnToolboxModel(string path, OpenTK.Mathematics.Vector3? drop)
	{
		if (!TryReadPlaceFile(path, out PlaceFile file) || file == null)
		{
			return;
		}
		string placeDir = null;
		try
		{
			placeDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
		}
		catch
		{
		}
		List<SceneObject> list = new List<SceneObject>();
		foreach (PartDto item in file.Parts ?? new List<PartDto>())
		{
			list.Add(DtoToPart(item, placeDir));
		}
		foreach (LightDto item2 in file.Lights ?? new List<LightDto>())
		{
			try
			{
				list.Add(DtoToLight(item2));
			}
			catch
			{
			}
		}
		if (list.Count == 0)
		{
			Log("Model is empty: " + System.IO.Path.GetFileName(path));
			return;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		foreach (SceneObject item3 in list)
		{
			if (!string.IsNullOrWhiteSpace(item3.TeleportLink))
			{
				if (!dictionary.TryGetValue(item3.TeleportLink, out var value))
				{
					value = $"Pad{++_teleportCounter}";
					dictionary[item3.TeleportLink] = value;
				}
				item3.TeleportLink = value;
			}
		}
		float num = list.Min((SceneObject o) => o.Position.X - o.Size.X / 2f);
		float num2 = list.Min((SceneObject o) => o.Position.Y - o.Size.Y / 2f);
		float num3 = list.Min((SceneObject o) => o.Position.Z - o.Size.Z / 2f);
		float num4 = list.Max((SceneObject o) => o.Position.X + o.Size.X / 2f);
		float num5 = list.Max((SceneObject o) => o.Position.Z + o.Size.Z / 2f);
		OpenTK.Mathematics.Vector3 vector = drop ?? InsertPoint();
		if (drop.HasValue)
		{
			OpenTK.Mathematics.Vector3 valueOrDefault = drop.GetValueOrDefault();
			if (SnapCheck.IsChecked == true)
			{
				OpenTK.Mathematics.Vector3 vector2 = SnapVec(new OpenTK.Mathematics.Vector3(valueOrDefault.X, 0f, valueOrDefault.Z));
				vector = new OpenTK.Mathematics.Vector3(vector2.X, valueOrDefault.Y, vector2.Z);
			}
		}
		OpenTK.Mathematics.Vector3 vector3 = vector - new OpenTK.Mathematics.Vector3((num + num4) / 2f, num2 + 0.05f, (num3 + num5) / 2f);
		PushUndo("Insert Model", mergeable: false);
		_partCounter += list.Count;
		foreach (SceneObject item4 in list)
		{
			item4.Position += vector3;
			for (int num6 = 0; num6 < item4.Waypoints.Count; num6++)
			{
				item4.Waypoints[num6] += vector3;
			}
			_scene.Objects.Add(item4);
			_physics.AddBody(item4);
		}
		_selection.Clear();
		foreach (SceneObject item5 in list)
		{
			_selection.Add(item5);
		}
		_scene.Selected = list[0];
		AfterSelectionChanged();
		_scene.InvalidateShadows();
		SetTool("Move");
		MarkDirty();
		_needsFrame = true;
		Log($"Inserted model {System.IO.Path.GetFileNameWithoutExtension(path)} ({list.Count} objects).");
		StatusText.Text = "Inserted model";
	}

	public void DefaultToolboxItem(string id)
	{
		if (_physics.Simulating || _playerMode)
		{
			Log("Toolbox: stop playing to spawn parts.");
			StatusText.Text = "Stop playing first";
		}
		else
		{
			SpawnToolboxItem(id, null, null);
		}
	}

	private void InsertToolboxPlane()
	{
		PushUndo("Insert", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Plane {_partCounter}",
			Shape = ShapeKind.Block,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(4f, 0.2f, 4f),
			Color = Palette[_scene.Objects.Count % Palette.Length]
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		Log("Inserted " + sceneObject.Name + " in front of camera.");
		StatusText.Text = "Inserted " + sceneObject.Name;
		MarkDirty();
	}

	private void InsertToolboxMesh()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Import mesh part",
			Filter = "3D models|*.fbx;*.obj|All files|*.*",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			_toolboxDropPoint = null;
			return;
		}
		PushUndo("Insert Mesh", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"Mesh {_partCounter}",
			Shape = ShapeKind.Mesh,
			MeshPath = openFileDialog.FileName,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(1f, 1f, 1f),
			Color = new Color4(1f, 1f, 1f, 1f),
			Material = MaterialKind.Plastic,
			Anchored = true
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		MarkDirty();
		Log($"Inserted {sceneObject.Name} from {openFileDialog.FileName} (renders the file, collides as a box).");
		StatusText.Text = "Inserted " + sceneObject.Name;
	}

	private void ApplyToolboxImage(string path, SceneObject? onPart, string face)
	{
		SceneObject sceneObject = onPart ?? _scene.Selected;
		if (sceneObject == null || sceneObject.Shape == ShapeKind.None)
		{
			Log("Image: drop onto a part (or select one first).");
			StatusText.Text = "Drop onto a part";
			return;
		}
		if (!File.Exists(path))
		{
			Log("Image missing: " + path);
			return;
		}
		sceneObject.MigrateSingleToList();
		if (sceneObject.Decals.Count >= 6)
		{
			StatusText.Text = $"Max {6} decals per part";
			Log("Image: " + sceneObject.Name + " already has its max decals.");
			return;
		}
		PushUndo("Decal", mergeable: false);
		SelectObject(sceneObject);
		sceneObject.Decals.Add(new DecalLayer
		{
			Image = path,
			Face = face
		});
		_liveRows = ObjectProperties(sceneObject);
		SetPropertyRows(_liveRows);
		_needsFrame = true;
		Log($"Decal applied to {sceneObject.Name} ({face}): {System.IO.Path.GetFileName(path)}");
		StatusText.Text = "Decal applied";
		MarkDirty();
	}

	private void ApplyToolboxSound(string path, SceneObject? onPart)
	{
		SceneObject sceneObject = onPart ?? _scene.Selected;
		if (sceneObject == null || sceneObject.Shape == ShapeKind.None)
		{
			Log("Sound: drop onto a part (or select one first).");
			StatusText.Text = "Drop onto a part";
			return;
		}
		if (!File.Exists(path))
		{
			Log("Sound missing: " + path);
			return;
		}
		PushUndo("Add sound", mergeable: false);
		SelectObject(sceneObject);
		sceneObject.SoundPath = path;
		sceneObject.SoundLoadedPath = null;
		sceneObject.SoundPlaying = true;
		_liveRows = ObjectProperties(sceneObject);
		SetPropertyRows(_liveRows);
		Log("Sound added to " + sceneObject.Name + ": " + System.IO.Path.GetFileName(path));
		StatusText.Text = "Sound added";
		MarkDirty();
		_needsFrame = true;
	}

	private void InsertShape(ShapeKind shape, string prefix)
	{
		PushUndo("Insert", mergeable: false);
		_partCounter++;
		OpenTK.Mathematics.Vector3 position = InsertPoint();
		SceneObject sceneObject = new SceneObject
		{
			Name = $"{prefix} {_partCounter}",
			Shape = shape,
			Position = position,
			Size = new OpenTK.Mathematics.Vector3(1f, 1f, 1f),
			Color = Palette[_scene.Objects.Count % Palette.Length]
		};
		_scene.Objects.Add(sceneObject);
		_physics.AddBody(sceneObject);
		_scene.InvalidateShadows();
		SelectObject(sceneObject);
		Log("Inserted " + sceneObject.Name + " in front of camera.");
		StatusText.Text = "Inserted " + sceneObject.Name;
		MarkDirty();
	}

	private void Play_Click(object sender, RoutedEventArgs e)
	{
		if (((sender as Fluent.Button)?.Header?.ToString() ?? "").Contains("Stop"))
		{
			StopPlaying();
		}
		else
		{
			StartPlaying();
		}
	}

	private void OnLoadedPlayerCheck(object? sender, RoutedEventArgs e)
	{
		base.Loaded -= OnLoadedPlayerCheck;
		string pendingPlayFile = App.PendingPlayFile;
		if (pendingPlayFile != null)
		{
			App.PendingPlayFile = null;
			EnterPlayerMode(pendingPlayFile);
		}
		else
		{
			ShowStartPage();
		}
	}

	public void EnterPlayerMode(string placePath)
	{
		_playerMode = true;
		if (!TryReadPlaceFile(placePath, out PlaceFile file, out string error) || file == null)
		{
			CrashLog.Startup($"player mode FAILED: {error ?? "unknown"} ({placePath})");
			MessageBox.Show("Couldn't open game file:\n" + placePath + "\n\n" + error, "Builder game", MessageBoxButton.OK, MessageBoxImage.Hand);
			Application.Current.Shutdown();
			return;
		}
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(placePath);
		base.Width = 1280.0;
		base.Height = 800.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		GettingStartedView.Visibility = Visibility.Collapsed;
		EditorView.Visibility = Visibility.Visible;
		MainRibbon.Visibility = Visibility.Collapsed;
		ExplorerPanel.Visibility = Visibility.Collapsed;
		ToolboxPanel.Visibility = Visibility.Collapsed;
		PropertiesPanel.Visibility = Visibility.Collapsed;
		ToolboxSplitter.Visibility = Visibility.Collapsed;
		ExplorerSplitter.Visibility = Visibility.Collapsed;
		PropertiesSplitter.Visibility = Visibility.Collapsed;
		LeftCol.Width = new GridLength(0.0);
		LeftCol.MinWidth = 0.0;
		LeftSplitCol.Width = new GridLength(0.0);
		RightCol.Width = new GridLength(0.0);
		RightCol.MinWidth = 0.0;
		RightSplitCol.Width = new GridLength(0.0);
		OutputPanel.Visibility = Visibility.Collapsed;
		OutputSplitter.Visibility = Visibility.Collapsed;
		OutputRow.Height = new GridLength(0.0);
		OutputRow.MinHeight = 0.0;
		StatusBar.Visibility = Visibility.Collapsed;
		ViewBar.Visibility = Visibility.Collapsed;
		ViewportInfoPanel.Visibility = Visibility.Collapsed;
		PauseGameName.Text = fileNameWithoutExtension;
		try
		{
			if (File.Exists(PublishedIconPath))
			{
				base.Icon = new BitmapImage(new Uri(PublishedIconPath));
			}
		}
		catch
		{
		}
		LoadPlace(file, placePath);
		base.Title = fileNameWithoutExtension;
		CrashLog.Startup($"player mode: {System.IO.Path.GetFileName(placePath)} ({_scene.Objects.Count} parts)");
		StartPlaying();
	}

	private bool TryReadPlaceFile(string path, out PlaceFile? file)
	{
		string error;
		return TryReadPlaceFile(path, out file, out error);
	}

	private bool TryReadPlaceFile(string path, out PlaceFile? file, out string? error)
	{
		file = null;
		error = null;
		try
		{
			file = JsonSerializer.Deserialize<PlaceFile>(File.ReadAllText(path), PlaceJson) ?? throw new InvalidDataException("Empty place file.");
		}
		catch (Exception ex)
		{
			error = ex.Message;
			Log("Open failed: " + ex.Message);
			StatusText.Text = "Open failed";
			return false;
		}
		if (file.Format < 1 || file.Format > 20)
		{
			error = $"unsupported place format {file.Format} (want 1-20)";
			Log("Open failed: " + error + ".");
			StatusText.Text = "Unsupported place format";
			return false;
		}
		return true;
	}

	private void TogglePause()
	{
		if (_playerMode && _physics.Simulating)
		{
			_paused = !_paused;
			PauseMenu.Visibility = ((!_paused) ? Visibility.Collapsed : Visibility.Visible);
			CancelCameraDrag();
			_needsFrame = true;
		}
	}

	private void Resume_Click(object sender, RoutedEventArgs e)
	{
		TogglePause();
	}

	private void Quit_Click(object sender, RoutedEventArgs e)
	{
		Application.Current.Shutdown();
	}

	private void GameSettings_Click(object sender, RoutedEventArgs e)
	{
		new GameSettingsWindow(this).ShowDialog();
		_needsFrame = true;
	}

	private void StartPlaying()
	{
		if (_physics.Simulating)
		{
			Log("Already playing.");
			return;
		}
		if (_scene.Objects.Count > 20000)
		{
			Log($"Play refused: {_scene.Objects.Count} parts is too many to simulate (limit {20000}). Delete the excess duplicates and try again.");
			StatusText.Text = "Too many parts to play";
			return;
		}
		_scene.PhysicsDriven = true;
		try
		{
			if (_physics.StartPlay(_scene, Log, AppSettings.Current.GravityStrength))
			{
				StatusText.Text = "? Playing...";
				CancelCameraDrag();
				SceneObject sceneObject = _scene.Objects.FirstOrDefault((SceneObject o) => o.IsSpawn);
				OpenTK.Mathematics.Vector3 vector = ((sceneObject != null) ? (sceneObject.Position + new OpenTK.Mathematics.Vector3(0f, sceneObject.Size.Y * 0.5f + 0.75f + 0.3f, 0f)) : new OpenTK.Mathematics.Vector3(0f, AppSettings.Current.PlayerSpawnHeight, 0f));
				SceneObject sceneObject2 = new SceneObject
				{
					Name = "Player",
					Shape = ShapeKind.Capsule,
					Position = vector,
					Size = new OpenTK.Mathematics.Vector3(1.5f, 1.5f, 1.5f),
					Color = new Color4(AppSettings.Current.AvatarR, AppSettings.Current.AvatarG, AppSettings.Current.AvatarB, 1f),
					Material = MaterialKind.Plastic,
					Anchored = false
				};
				_scene.Objects.Add(sceneObject2);
				_physics.SpawnPoint = vector;
				string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Faces");
				_faceOpenPath = System.IO.Path.Combine(path, "face.png");
				_faceBlinkPath = System.IO.Path.Combine(path, "blink.png");
				if (!File.Exists(_faceOpenPath) || !File.Exists(_faceBlinkPath))
				{
					Log("Avatar face skipped: Faces/face.png or blink.png missing next to the exe.");
					_faceOpenPath = (_faceBlinkPath = null);
				}
				else
				{
					SceneObject sceneObject3 = new SceneObject
					{
						Name = "Face",
						Shape = ShapeKind.Block,
						Position = vector + _physics.FaceOffset,
						Size = new OpenTK.Mathematics.Vector3(0.7f, 0.7f, 0.05f),
						Color = new Color4(0.2f, 0.5f, 1f, 1f),
						Anchored = true,
						CanCollide = false,
						IsAvatarFace = true,
						Transparency = 1f
					};
					sceneObject3.Decals.Add(new DecalLayer
					{
						Image = _faceOpenPath,
						Face = "Front"
					});
					_scene.Objects.Add(sceneObject3);
					_physics.FaceObject = sceneObject3;
					foreach (SceneObject item in _scene.Objects.Where((SceneObject o) => o.IsNpc).ToList())
					{
						SceneObject sceneObject4 = new SceneObject
						{
							Name = item.Name + " Face",
							Shape = ShapeKind.Block,
							Position = item.Position + OpenTK.Mathematics.Vector3.Transform(new OpenTK.Mathematics.Vector3(0f, 0.2f, 0.38f), item.Orientation),
							Size = new OpenTK.Mathematics.Vector3(0.7f, 0.7f, 0.05f),
							Color = item.Color,
							Anchored = true,
							CanCollide = false,
							IsAvatarFace = true,
							Transparency = 1f
						};
						string image = _faceOpenPath;
						if (!string.IsNullOrWhiteSpace(item.NpcFaceImage))
						{
							if (File.Exists(item.NpcFaceImage))
							{
								image = item.NpcFaceImage;
							}
							else
							{
								Log(item.Name + " face image missing, using stock face: " + item.NpcFaceImage);
							}
						}
						sceneObject4.Decals.Add(new DecalLayer
						{
							Image = image,
							Face = "Front"
						});
						_scene.Objects.Add(sceneObject4);
						_npcFaces[item] = sceneObject4;
					}
				}
				_playStartUtc = DateTime.UtcNow;
				_physics.SpawnPlayer(sceneObject2);
				_lastHealthShown = -1f;
				_damageFlashStartUtc = DateTime.MinValue;
				UpdateHealthBar(force: true);
				_scene.FollowTarget = sceneObject2;
				_scene.FollowDistance = AppSettings.Current.PlayerZoom;
				_scene.ResetFollowDistance();
				_physics.PlayerCamYaw = _scene.Yaw;
				SelectObject(null);
				ToolboxTabs.SetPlayMode(playing: true);
				_scene.SelWaypointObj = null;
				_scene.SelWaypointIndex = -1;
				_scene.InvalidateShadows();
				_needsFrame = true;
				_scripts = new ScriptScheduler(_scene, _physics, delegate(string msg)
				{
					Log(msg);
				}, delegate
				{
					_needsFrame = true;
				}, delegate
				{
					RefreshExplorer();
				}, SpawnScriptPart, RemoveScriptPart);
				_scripts.Start();
				if (AppSettings.Current.BlockyAvatar)
				{
					EnableAvatarRig();
				}
				Log($"Play: Bepu simulation running (gravity {AppSettings.Current.GravityStrength}).");
				Log((sceneObject != null) ? ("Spawned Player on " + sceneObject.Name + ": WASD move \u00B7 Space jump \u00B7 drag to orbit \u00B7 wheel to zoom.") : "Spawned Player (blue capsule 1.5): WASD move \u00B7 Space jump \u00B7 drag to orbit \u00B7 wheel to zoom.");
			}
			else
			{
				_scene.PhysicsDriven = false;
			}
		}
		catch (Exception ex)
		{
			_physics.StopPlay(_scene);
			_scene.PhysicsDriven = false;
			StatusText.Text = "Physics failed";
			Log("Play failed: " + ex.ToString());
		}
	}

	public void ApplyBlockyAvatar(bool on)
	{
		AppSettings.Current.BlockyAvatar = on;
		AppSettings.Current.Save();
		if (_physics.Simulating)
		{
			if (on)
			{
				EnableAvatarRig();
			}
			else
			{
				DisableAvatarRig();
			}
		}
	}

	private void EnableAvatarRig()
	{
		if (_physics.Player != null)
		{
			if (_avatarRig == null)
			{
				_avatarRig = new AvatarRig();
			}
			_avatarRig.Attach(_physics.Player, _physics.FaceObject, _scene.AvatarRigParts);
			_needsFrame = true;
		}
	}

	private void DisableAvatarRig()
	{
		if (_avatarRig != null)
		{
			_avatarRig.Detach(_physics.Player, _physics.FaceObject, _scene.AvatarRigParts);
			_avatarRig = null;
			_needsFrame = true;
		}
	}

	private void StopPlaying()
	{
		DisableAvatarRig();
		_scripts?.Stop();
		if (_physics.Simulating)
		{
			if (_voidDeleted.Count > 0)
			{
				foreach (SceneObject item in _voidDeleted)
				{
					if (!_scene.Objects.Contains(item))
					{
						_scene.Objects.Add(item);
					}
					item.BreakCount = 0;
					item.BreakCooldown = -1.0;
				}
				Log($"Restored {_voidDeleted.Count} removed part(s) (void + shattered).");
				_voidDeleted.Clear();
			}
			SceneObject player = _physics.Player;
			SceneObject faceObject = _physics.FaceObject;
			_physics.StopPlay(_scene);
			if (player != null)
			{
				_scene.Objects.Remove(player);
			}
			if (faceObject != null)
			{
				_scene.Objects.Remove(faceObject);
			}
			foreach (SceneObject value in _npcFaces.Values)
			{
				_scene.Objects.Remove(value);
			}
			_npcFaces.Clear();
			_scene.FollowTarget = null;
			_scene.PhysicsDriven = false;
			ClearNpcBubbles();
			HealthBar.Visibility = Visibility.Collapsed;
			DamageVignette.Visibility = Visibility.Collapsed;
			DamageVignette.Opacity = 0.0;
			_lastHealthShown = -1f;
			CancelCameraDrag();
			if (_scene.Selected == player)
			{
				SelectObject(null);
			}
			else
			{
				RefreshExplorer();
			}
			_scene.InvalidateShadows();
			_needsFrame = true;
			ToolboxTabs.SetPlayMode(playing: false);
			StatusText.Text = "? Stopped";
			Log("Stopped: poses restored.");
			AudioEngine.StopAllPartSounds();
		}
		else
		{
			StatusText.Text = "? Stopped";
			Log("Stop: nothing playing.");
		}
	}

	private void UpdateHealthBar(bool force = false)
	{
		if (_physics.Player != null)
		{
			float num = Math.Clamp(_physics.Health, 0f, _physics.MaxHealth);
			float num2 = ((_physics.MaxHealth > 0f) ? (num / _physics.MaxHealth) : 0f);
			DateTime utcNow = DateTime.UtcNow;
			if (_physics.DiedThisStep)
			{
				_damageFlashStartUtc = utcNow;
			}
			else if (_lastHealthShown >= 0f && num < _lastHealthShown - 0.001f)
			{
				_damageFlashStartUtc = utcNow;
			}
			if (!force && Math.Abs(num - _lastHealthShown) < 0.5f && !IsFlashActive(utcNow))
			{
				UpdateVignette(num2, utcNow);
				return;
			}
			_lastHealthShown = num;
			HealthFill.Width = 140f * num2;
			HealthText.Text = $"{Math.Ceiling(num)}";
			string value = ((num2 > 0.5f) ? "#FF33CC66" : ((num2 > 0.25f) ? "#FFFFBB33" : "#FFFF4444"));
			HealthFill.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
			HealthBar.Visibility = ((!(num2 < 0.999f)) ? Visibility.Collapsed : Visibility.Visible);
			UpdateVignette(num2, utcNow);
		}
	}

	private bool IsFlashActive(DateTime now)
	{
		return (now - _damageFlashStartUtc).TotalSeconds < 0.7;
	}

	private void UpdateVignette(float frac, DateTime now)
	{
		double totalSeconds = (now - _damageFlashStartUtc).TotalSeconds;
		float num = ((totalSeconds < 0.7) ? ((float)(1.0 - totalSeconds / 0.7)) : 0f);
		float num2 = Math.Clamp((1f - frac) * 0.45f + num * 0.6f, 0f, 0.85f);
		if (num2 <= 0.01f)
		{
			DamageVignette.Visibility = Visibility.Collapsed;
			DamageVignette.Opacity = 0.0;
		}
		else
		{
			DamageVignette.Visibility = Visibility.Visible;
			DamageVignette.Opacity = num2;
		}
	}

	private static int CompareBubbleKey((PhysicsWorld.NpcBubble Bubble, float Key) a, (PhysicsWorld.NpcBubble Bubble, float Key) b)
	{
		return a.Key.CompareTo(b.Key);
	}

	private void UpdateNpcBubbles()
	{
		if (!_physics.Simulating || _physics.Player == null)
		{
			ClearNpcBubbles();
			return;
		}
		if (_physics.ActiveBubbles.Count == 0)
		{
			if (_npcBubbleViews.Count > 0)
			{
				ClearNpcBubbles();
			}
			return;
		}
		double actualWidth = ViewportControl.ActualWidth;
		double actualHeight = ViewportControl.ActualHeight;
		if (actualWidth < 10.0 || actualHeight < 10.0)
		{
			return;
		}
		_bubbleWork.Clear();
		foreach (PhysicsWorld.NpcBubble activeBubble in _physics.ActiveBubbles)
		{
			if (!activeBubble.Npc.IsAvatarFace)
			{
				_bubbleWork.Add((activeBubble, (activeBubble.Npc.Position - _scene.Position).LengthSquared));
			}
		}
		if (_bubbleWork.Count > 5)
		{
			_bubbleWork.Sort(CompareBubbleKey);
			while (_bubbleWork.Count > 5)
			{
				_bubbleWork.RemoveAt(_bubbleWork.Count - 1);
			}
		}
		_bubbleSeen.Clear();
		foreach (var item2 in _bubbleWork)
		{
			PhysicsWorld.NpcBubble item = item2.Bubble;
			SceneObject npc = item.Npc;
			OpenTK.Mathematics.Vector3 world = npc.Position + new OpenTK.Mathematics.Vector3(0f, npc.Size.Y * 0.5f + 0.9f, 0f);
			if (!_scene.WorldToScreen(world, (float)actualWidth, (float)actualHeight, out var sx, out var sy) || sx < -200f || (double)sx > actualWidth + 200.0 || sy < -120f || (double)sy > actualHeight + 60.0)
			{
				if (_npcBubbleViews.TryGetValue(npc, out Border value))
				{
					NpcBubbleLayer.Children.Remove(value);
					_npcBubbleViews.Remove(npc);
				}
				continue;
			}
			_bubbleSeen.Add(npc);
			if (!_npcBubbleViews.TryGetValue(npc, out Border value2))
			{
				value2 = MakeBubbleCard(npc);
				_npcBubbleViews[npc] = value2;
				NpcBubbleLayer.Children.Add(value2);
			}
			SyncBubbleCard(value2, npc, item.Line);
			double num = item.ExpiresAt - _physics.SimTime;
			value2.Opacity = Math.Clamp(num / 0.8, 0.15, 1.0);
			if (!object.Equals(value2.Tag, item.Line))
			{
				value2.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
				value2.Tag = item.Line;
			}
			System.Windows.Size desiredSize = value2.DesiredSize;
			double length = Math.Clamp((double)sx - desiredSize.Width / 2.0, 4.0, Math.Max(4.0, actualWidth - desiredSize.Width - 4.0));
			double length2 = Math.Clamp((double)sy - desiredSize.Height - 12.0, 4.0, Math.Max(4.0, actualHeight - desiredSize.Height - 4.0));
			Canvas.SetLeft(value2, length);
			Canvas.SetTop(value2, length2);
		}
		_staleBubbleKeys.Clear();
		foreach (SceneObject key in _npcBubbleViews.Keys)
		{
			if (!_bubbleSeen.Contains(key))
			{
				_staleBubbleKeys.Add(key);
			}
		}
		foreach (SceneObject staleBubbleKey in _staleBubbleKeys)
		{
			NpcBubbleLayer.Children.Remove(_npcBubbleViews[staleBubbleKey]);
			_npcBubbleViews.Remove(staleBubbleKey);
		}
	}

	private static Border MakeBubbleCard(SceneObject npc)
	{
		bool flag = npc.NpcType == NpcKind.Enemy;
		return new Border
		{
			Background = new SolidColorBrush(flag ? System.Windows.Media.Color.FromRgb(byte.MaxValue, 232, 232) : System.Windows.Media.Color.FromRgb(253, 253, 253)),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(110, 110, 110)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(8.0, 5.0, 8.0, 6.0),
			MaxWidth = 220.0,
			Child = new StackPanel
			{
				Children = 
				{
					(UIElement)new TextBlock
					{
						FontWeight = FontWeights.Bold,
						FontSize = 11.0,
						Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(90, 90, 90))
					},
					(UIElement)new TextBlock
					{
						FontSize = 12.0,
						TextWrapping = TextWrapping.Wrap,
						Foreground = new SolidColorBrush(Colors.Black)
					}
				}
			}
		};
	}

	private static void SyncBubbleCard(Border card, SceneObject npc, string line)
	{
		if (!(card.Child is StackPanel stackPanel) || stackPanel.Children.Count < 2)
		{
			return;
		}
		if (stackPanel.Children[0] is TextBlock textBlock && stackPanel.Children[1] is TextBlock textBlock2)
		{
			string text = ((npc.NpcType == NpcKind.Enemy) ? ("?? " + npc.Name) : npc.Name);
			if (textBlock.Text != text)
			{
				textBlock.Text = text;
			}
			if (textBlock2.Text != line)
			{
				textBlock2.Text = line;
			}
		}
		System.Windows.Media.Color color = ((npc.NpcType == NpcKind.Enemy) ? System.Windows.Media.Color.FromRgb(byte.MaxValue, 232, 232) : System.Windows.Media.Color.FromRgb(253, 253, 253));
		if (card.Background is SolidColorBrush solidColorBrush && solidColorBrush.Color != color)
		{
			card.Background = new SolidColorBrush(color);
		}
	}

	private void ClearNpcBubbles()
	{
		if (_npcBubbleViews.Count != 0)
		{
			NpcBubbleLayer.Children.Clear();
			_npcBubbleViews.Clear();
		}
	}

	private SceneSnapshot CaptureState(string tag)
	{
		return new SceneSnapshot
		{
			Label = tag,
			MergeKey = tag,
			Parts = _scene.Objects.Select((SceneObject o) => o.Clone(o.Name)).ToList(),
			SelectedIndex = ((_scene.Selected != null) ? _scene.Objects.IndexOf(_scene.Selected) : (-1)),
			MemberIndices = (from t in _scene.Objects.Select((SceneObject o, int i) => (o: o, i: i))
				where _selection.Contains(t.o)
				select t.i).ToList(),
			PartCounter = _partCounter,
			SunDirection = _scene.SunDirection,
			SunColor = _scene.SunColor,
			SunIntensity = _scene.SunIntensity,
			Shadows = _scene.ShadowsEnabled,
			Ambient = _scene.AmbientBoost,
			FogDensity = _scene.FogDensity,
			FogColor = _scene.FogColor,
			Haze = _scene.SkyHaze,
			Time = _scene.TimeOfDay,
			SkyTint = _scene.SkyTint,
			StarAmount = _scene.StarAmount,
			CloudAmount = _scene.CloudAmount,
			SunSize = _scene.SunSize,
			SunStripes = _scene.SunStripes,
			SkyStyle = _scene.SkyStyle,
			SkyPresetName = _skyPresetName
		};
	}

	private void PushCaptured(SceneSnapshot snap, bool mergeable)
	{
		if (_restoringHistory || _physics.Simulating)
		{
			return;
		}
		if (mergeable)
		{
			DateTime utcNow = DateTime.UtcNow;
			if (snap.MergeKey == _lastUndoKey && (utcNow - _lastUndoTime).TotalSeconds < 2.0)
			{
				return;
			}
			_lastUndoKey = snap.MergeKey;
			_lastUndoTime = utcNow;
		}
		if (_undo.Count >= 100)
		{
			_undo.RemoveAt(0);
		}
		_undo.Add(snap);
		_redo.Clear();
		UpdateUndoButtons();
	}

	private void PushUndo(string tag, bool mergeable = true)
	{
		PushCaptured(CaptureState(tag), mergeable);
	}

	private void RestoreState(SceneSnapshot snap)
	{
		_restoringHistory = true;
		try
		{
			_scene.Objects.Clear();
			foreach (SceneObject part in snap.Parts)
			{
				_scene.Objects.Add(part.Clone(part.Name));
			}
			_partCounter = snap.PartCounter;
			TimeSlider.Value = snap.Time;
			_scene.SunDirection = snap.SunDirection;
			_scene.SunColor = snap.SunColor;
			_scene.SunIntensity = snap.SunIntensity;
			ApplyShadows(snap.Shadows);
			_scene.AmbientBoost = snap.Ambient;
			ApplyFog(snap.FogDensity);
			_scene.FogColor = snap.FogColor;
			_scene.SkyHaze = snap.Haze;
			_scene.SkyTint = snap.SkyTint;
			_scene.StarAmount = snap.StarAmount;
			_scene.CloudAmount = snap.CloudAmount;
			_scene.SunSize = snap.SunSize;
			_scene.SunStripes = snap.SunStripes;
			_scene.SkyStyle = snap.SkyStyle;
			_skyPresetName = snap.SkyPresetName;
			_lastUndoKey = null;
			_selection.Clear();
			foreach (int memberIndex in snap.MemberIndices)
			{
				if (memberIndex >= 0 && memberIndex < _scene.Objects.Count)
				{
					_selection.Add(_scene.Objects[memberIndex]);
				}
			}
			_scene.Selected = ((snap.SelectedIndex >= 0 && snap.SelectedIndex < _scene.Objects.Count) ? _scene.Objects[snap.SelectedIndex] : _selection.FirstOrDefault());
			if (_scene.Selected != null)
			{
				_selection.Add(_scene.Selected);
			}
			AfterSelectionChanged();
			_scene.InvalidateShadows();
			_needsFrame = true;
			MarkDirty();
		}
		finally
		{
			_restoringHistory = false;
		}
		UpdateUndoButtons();
	}

	private void UpdateUndoButtons()
	{
		UndoButton.IsEnabled = _undo.Count > 0;
		RedoButton.IsEnabled = _redo.Count > 0;
	}

	private void Undo_Click(object sender, RoutedEventArgs e)
	{
		Undo();
	}

	private void Redo_Click(object sender, RoutedEventArgs e)
	{
		Redo();
	}

	private void Undo()
	{
		if (_physics.Simulating)
		{
			StatusText.Text = "Stop playing to undo";
			Log("Undo: stop playing first.");
			return;
		}
		if (_undo.Count == 0)
		{
			StatusText.Text = "Nothing to undo";
			return;
		}
		List<SceneSnapshot> undo = _undo;
		SceneSnapshot sceneSnapshot = undo[undo.Count - 1];
		_undo.RemoveAt(_undo.Count - 1);
		_redo.Add(CaptureState(sceneSnapshot.Label));
		RestoreState(sceneSnapshot);
		Log("Undo " + sceneSnapshot.Label + ".");
		StatusText.Text = "Undo " + sceneSnapshot.Label;
		UpdateUndoButtons();
	}

	private void Redo()
	{
		if (_physics.Simulating)
		{
			StatusText.Text = "Stop playing to redo";
			Log("Redo: stop playing first.");
			return;
		}
		if (_redo.Count == 0)
		{
			StatusText.Text = "Nothing to redo";
			return;
		}
		List<SceneSnapshot> redo = _redo;
		SceneSnapshot sceneSnapshot = redo[redo.Count - 1];
		_redo.RemoveAt(_redo.Count - 1);
		SceneSnapshot item = CaptureState(sceneSnapshot.Label);
		if (_undo.Count >= 100)
		{
			_undo.RemoveAt(0);
		}
		_undo.Add(item);
		RestoreState(sceneSnapshot);
		Log("Redo " + sceneSnapshot.Label + ".");
		StatusText.Text = "Redo " + sceneSnapshot.Label;
		UpdateUndoButtons();
	}

	private void MarkDirty()
	{
		if (!_dirty)
		{
			_dirty = true;
			UpdateTitle();
		}
	}

	private void UpdateTitle()
	{
		string text = ((_savePath != null) ? System.IO.Path.GetFileName(_savePath) : "Untitled");
		base.Title = "builder engine : " + text + (_dirty ? " \u2022" : "");
	}

	private static float[] V3(OpenTK.Mathematics.Vector3 v)
	{
		return new float[3] { v.X, v.Y, v.Z };
	}

	private static OpenTK.Mathematics.Vector3 F3(float[]? a, OpenTK.Mathematics.Vector3 fallback)
	{
		if (a == null || a.Length != 3)
		{
			return fallback;
		}
		return new OpenTK.Mathematics.Vector3(a[0], a[1], a[2]);
	}

	private static OpenTK.Mathematics.Vector3 ClampSize(OpenTK.Mathematics.Vector3 s)
	{
		return new OpenTK.Mathematics.Vector3(Math.Max(s.X, 0.01f), Math.Max(s.Y, 0.01f), Math.Max(s.Z, 0.01f));
	}

	private static float Clamp01(float v)
	{
		return Math.Clamp(v, 0f, 1f);
	}

	private static string? ResolveAsset(string? p, string? dir)
	{
		if (string.IsNullOrWhiteSpace(p) || System.IO.Path.IsPathRooted(p) || dir == null)
		{
			return p;
		}
		try
		{
			return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, p));
		}
		catch
		{
			return p;
		}
	}

	private static string? RelativizeAsset(string? p, string? dir)
	{
		if (string.IsNullOrWhiteSpace(p) || dir == null || !System.IO.Path.IsPathRooted(p))
		{
			return p;
		}
		try
		{
			string relativePath = System.IO.Path.GetRelativePath(dir, p);
			if (!relativePath.StartsWith("..", StringComparison.Ordinal))
			{
				return relativePath;
			}
		}
		catch
		{
		}
		return p;
	}

	private PartDto CapturePart(SceneObject o, string? relativizeDir)
	{
		PartDto partDto = new PartDto();
		partDto.Name = o.Name;
		partDto.Shape = o.Shape;
		partDto.Position = V3(o.Position);
		partDto.Size = V3(o.Size);
		partDto.Rotation = V3(o.Rotation);
		partDto.Color = new float[3]
		{
			o.Color.R,
			o.Color.G,
			o.Color.B
		};
		partDto.Material = o.Material;
		partDto.Anchored = o.Anchored;
		partDto.IsSpawn = o.IsSpawn;
		partDto.IsNpc = o.IsNpc;
		partDto.NpcType = o.NpcType;
		partDto.NpcDamage = o.NpcDamage;
		partDto.NpcSpeed = o.NpcSpeed;
		partDto.NpcDialogue = o.NpcDialogue;
		partDto.NpcChatRange = o.NpcChatRange;
		partDto.NpcChatInterval = o.NpcChatInterval;
		partDto.NpcFaceImage = RelativizeAsset(o.NpcFaceImage, relativizeDir);
		partDto.Damage = o.Damage;
		partDto.Tiling = o.Tiling;
		partDto.IsWater = o.IsWater;
		partDto.Hidden = o.Hidden;
		partDto.IsEmitter = o.IsEmitter;
		partDto.ParticlePreset = o.ParticlePreset;
		partDto.EmissionRate = o.EmissionRate;
		partDto.ParticleLifetime = o.ParticleLifetime;
		partDto.ParticleSpeed = o.ParticleSpeed;
		partDto.ParticleSize = o.ParticleSize;
		partDto.ParticleSpread = o.ParticleSpread;
		partDto.ParticleGravity = o.ParticleGravity;
		partDto.Locked = o.Locked;
		partDto.IsMagnet = o.IsMagnet;
		partDto.MagnetPower = o.MagnetPower;
		partDto.MagnetRange = o.MagnetRange;
		partDto.IsConveyor = o.IsConveyor;
		partDto.ConveyorDirection = V3(o.ConveyorDirection);
		partDto.ConveyorSpeed = o.ConveyorSpeed;
		partDto.IsMovingPlatform = o.IsMovingPlatform;
		partDto.MoveDistance = o.MoveDistance;
		partDto.MoveSpeed = o.MoveSpeed;
		partDto.Waypoints = o.Waypoints.Select((OpenTK.Mathematics.Vector3 w) => new float[3] { w.X, w.Y, w.Z }).ToList();
		partDto.IsSpinner = o.IsSpinner;
		partDto.SpinSpeed = o.SpinSpeed;
		partDto.IsBreakable = o.IsBreakable;
		partDto.BreakHits = o.BreakHits;
		partDto.TeleportLink = o.TeleportLink;
		partDto.IsCheckpoint = o.IsCheckpoint;
		partDto.IsBouncePad = o.IsBouncePad;
		partDto.BouncePower = o.BouncePower;
		partDto.IsTimedPart = o.IsTimedPart;
		partDto.TimedHidden = o.TimedHidden;
		partDto.TimedOffset = o.TimedOffset;
		partDto.TimedVisible = o.TimedVisible;
		partDto.VelocityDirection = V3(o.VelocityDirection);
		partDto.VelocitySpeed = o.VelocitySpeed;
		partDto.VelocityMode = (int)o.VelocityMode;
		partDto.Mass = o.Mass;
		partDto.CanCollide = o.CanCollide;
		partDto.Transparency = o.Transparency;
		partDto.Reflectance = o.Reflectance;
		partDto.ColorBrightness = o.ColorBrightness;
		partDto.CastShadow = o.CastShadow;
		partDto.DecalImage = RelativizeAsset(o.DecalImage, relativizeDir);
		partDto.DecalFace = o.DecalFace;
		partDto.DecalTransparency = o.DecalTransparency;
		partDto.DecalBlur = o.DecalBlur;
		partDto.SoundPath = RelativizeAsset(o.SoundPath, relativizeDir);
		partDto.SoundVolume = o.SoundVolume;
		partDto.SoundLooped = o.SoundLooped;
		partDto.SoundPlaying = o.SoundPlaying;
		partDto.Script = o.Script ?? "";
		partDto.SoundPlayOnTouch = o.SoundPlayOnTouch;
		partDto.MeshPath = RelativizeAsset(o.MeshPath, relativizeDir);
		partDto.CollisionFidelity = o.CollisionFidelity;
		partDto.RenderFidelity = o.RenderFidelity;
		PartDto partDto2 = partDto;
		foreach (DecalLayer decal in o.Decals)
		{
			if (!string.IsNullOrWhiteSpace(decal.Image))
			{
				partDto2.Decals.Add(new DecalDto
				{
					Image = (RelativizeAsset(decal.Image, relativizeDir) ?? decal.Image),
					Face = decal.Face,
					Transparency = decal.Transparency,
					Blur = decal.Blur,
					OffsetX = decal.OffsetX,
					OffsetY = decal.OffsetY,
					Scale = decal.Scale
				});
			}
		}
		foreach (TextureLayer texture in o.Textures)
		{
			if (!string.IsNullOrWhiteSpace(texture.Image))
			{
				partDto2.Textures.Add(new TextureDto
				{
					Image = (RelativizeAsset(texture.Image, relativizeDir) ?? texture.Image),
					Face = texture.Face,
					Transparency = texture.Transparency,
					TilingX = texture.TilingX,
					TilingY = texture.TilingY,
					OffsetX = texture.OffsetX,
					OffsetY = texture.OffsetY
				});
			}
		}
		foreach (TextLayer text in o.Texts)
		{
			partDto2.Texts.Add(new TextDto
			{
				Text = text.Text,
				Face = text.Face,
				Font = text.Font,
				Size = text.Size,
				Color = text.Color,
				Transparency = text.Transparency,
				Blur = text.Blur,
				OffsetX = text.OffsetX,
				OffsetY = text.OffsetY,
				Scale = text.Scale,
				Bold = text.Bold,
				Outline = text.Outline,
				OutlineColor = text.OutlineColor
			});
		}
		return partDto2;
	}

	private LightDto CaptureLight(SceneObject o)
	{
		LightDto lightDto = new LightDto();
		lightDto.Name = o.Name;
		lightDto.Kind = o.Light;
		lightDto.Position = V3(o.Position);
		lightDto.Color = new float[3]
		{
			o.Color.R,
			o.Color.G,
			o.Color.B
		};
		lightDto.Brightness = o.Brightness;
		lightDto.Range = o.Range;
		return lightDto;
	}

	private SceneObject DtoToPart(PartDto p, string? placeDir)
	{
		SceneObject sceneObject = new SceneObject();
		sceneObject.Name = (string.IsNullOrWhiteSpace(p.Name) ? "Part" : p.Name);
		sceneObject.Shape = (Enum.IsDefined(p.Shape) ? p.Shape : ShapeKind.Block);
		sceneObject.Position = F3(p.Position, OpenTK.Mathematics.Vector3.Zero);
		sceneObject.Size = ClampSize(F3(p.Size, new OpenTK.Mathematics.Vector3(2f, 2f, 2f)));
		sceneObject.Rotation = F3(p.Rotation, OpenTK.Mathematics.Vector3.Zero);
		sceneObject.Color = new Color4(Clamp01(F3(p.Color, new OpenTK.Mathematics.Vector3(0.25f, 0.55f, 0.95f)).X), Clamp01(F3(p.Color, new OpenTK.Mathematics.Vector3(0.25f, 0.55f, 0.95f)).Y), Clamp01(F3(p.Color, new OpenTK.Mathematics.Vector3(0.25f, 0.55f, 0.95f)).Z), 1f);
		sceneObject.Material = (Enum.IsDefined(p.Material) ? p.Material : MaterialKind.Plastic);
		sceneObject.Anchored = p.Anchored;
		sceneObject.IsSpawn = p.IsSpawn;
		sceneObject.IsNpc = p.IsNpc;
		sceneObject.NpcType = (Enum.IsDefined(p.NpcType) ? p.NpcType : NpcKind.Enemy);
		sceneObject.NpcDamage = Math.Clamp(p.NpcDamage, 0f, 100f);
		sceneObject.NpcSpeed = Math.Clamp((p.NpcSpeed <= 0f) ? 3f : p.NpcSpeed, 0.5f, 12f);
		sceneObject.NpcDialogue = (string.IsNullOrWhiteSpace(p.NpcDialogue) ? "Hello!" : p.NpcDialogue.Substring(0, Math.Min(800, p.NpcDialogue.Length)));
		sceneObject.NpcChatRange = Math.Clamp((p.NpcChatRange <= 0f) ? 10f : p.NpcChatRange, 4f, 40f);
		sceneObject.NpcChatInterval = Math.Clamp((p.NpcChatInterval <= 0f) ? 5f : p.NpcChatInterval, 2f, 20f);
		sceneObject.NpcFaceImage = ResolveAsset(string.IsNullOrWhiteSpace(p.NpcFaceImage) ? null : p.NpcFaceImage, placeDir);
		sceneObject.Damage = Math.Clamp(p.Damage, 0f, 1000f);
		sceneObject.Tiling = Math.Clamp((p.Tiling <= 0f) ? 1f : p.Tiling, 0.1f, 8f);
		sceneObject.IsWater = p.IsWater;
		sceneObject.Hidden = p.Hidden;
		sceneObject.IsEmitter = p.IsEmitter || (!string.IsNullOrEmpty(p.ParticlePreset) && p.ParticlePreset != "None");
		sceneObject.ParticlePreset = ((!string.IsNullOrEmpty(p.ParticlePreset)) ? p.ParticlePreset : (p.IsEmitter ? "Custom" : "None"));
		sceneObject.EmissionRate = Math.Clamp(p.EmissionRate, 0f, 200f);
		sceneObject.ParticleLifetime = Math.Clamp((p.ParticleLifetime <= 0f) ? 1.5f : p.ParticleLifetime, 0.1f, 10f);
		sceneObject.ParticleSpeed = Math.Clamp(p.ParticleSpeed, 0f, 50f);
		sceneObject.ParticleSize = Math.Clamp((p.ParticleSize <= 0f) ? 0.5f : p.ParticleSize, 0.1f, 4f);
		sceneObject.ParticleSpread = Math.Clamp(p.ParticleSpread, 0f, 1f);
		sceneObject.ParticleGravity = Math.Clamp(p.ParticleGravity, -20f, 20f);
		sceneObject.Locked = p.Locked;
		sceneObject.IsMagnet = p.IsMagnet;
		sceneObject.MagnetPower = Math.Clamp(p.MagnetPower, 0f, 100f);
		sceneObject.MagnetRange = Math.Clamp((p.MagnetRange <= 0f) ? 10f : p.MagnetRange, 1f, 100f);
		sceneObject.IsConveyor = p.IsConveyor;
		sceneObject.ConveyorDirection = F3(p.ConveyorDirection, OpenTK.Mathematics.Vector3.UnitX);
		sceneObject.ConveyorSpeed = Math.Clamp(p.ConveyorSpeed, 0f, 100f);
		sceneObject.IsMovingPlatform = p.IsMovingPlatform;
		sceneObject.MoveDistance = Math.Clamp(p.MoveDistance, 0f, 200f);
		sceneObject.MoveSpeed = Math.Clamp(p.MoveSpeed, 0f, 50f);
		sceneObject.IsSpinner = p.IsSpinner;
		sceneObject.SpinSpeed = Math.Clamp(p.SpinSpeed, -720f, 720f);
		sceneObject.IsBreakable = p.IsBreakable;
		sceneObject.BreakHits = Math.Clamp((p.BreakHits <= 0) ? 2 : p.BreakHits, 1, 99);
		sceneObject.TeleportLink = (string.IsNullOrWhiteSpace(p.TeleportLink) ? "" : p.TeleportLink.Trim().Substring(0, Math.Min(24, p.TeleportLink.Trim().Length)));
		sceneObject.IsCheckpoint = p.IsCheckpoint;
		sceneObject.IsBouncePad = p.IsBouncePad;
		sceneObject.BouncePower = Math.Clamp((p.BouncePower <= 0f) ? 20f : p.BouncePower, 0f, 50f);
		sceneObject.IsTimedPart = p.IsTimedPart;
		sceneObject.TimedHidden = Math.Clamp(p.TimedHidden, 0f, 3600f);
		sceneObject.TimedOffset = Math.Clamp(p.TimedOffset, -3600f, 3600f);
		sceneObject.TimedVisible = Math.Clamp(p.TimedVisible, 0f, 3600f);
		sceneObject.VelocityDirection = F3(p.VelocityDirection, OpenTK.Mathematics.Vector3.Zero);
		sceneObject.VelocitySpeed = Math.Clamp(p.VelocitySpeed, 0f, 100f);
		SceneObject sceneObject2 = sceneObject;
		int velocityMode = p.VelocityMode;
		bool flag = (uint)velocityMode <= 1u;
		sceneObject2.VelocityMode = (flag ? ((SpeedMode)p.VelocityMode) : SpeedMode.Accelerating);
		sceneObject.Mass = Math.Clamp((p.Mass <= 0f) ? 1f : p.Mass, 0.01f, 100f);
		sceneObject.CanCollide = p.CanCollide;
		sceneObject.Transparency = Clamp01(p.Transparency);
		sceneObject.Reflectance = Clamp01(p.Reflectance);
		sceneObject.ColorBrightness = Math.Clamp(p.ColorBrightness, 0f, 50f);
		sceneObject.CastShadow = p.CastShadow;
		sceneObject.DecalImage = ResolveAsset(string.IsNullOrWhiteSpace(p.DecalImage) ? null : p.DecalImage, placeDir);
		sceneObject.DecalFace = p.DecalFace;
		sceneObject.DecalTransparency = Clamp01(p.DecalTransparency);
		sceneObject.DecalBlur = Clamp01(p.DecalBlur);
		sceneObject.SoundPath = ResolveAsset(string.IsNullOrWhiteSpace(p.SoundPath) ? null : p.SoundPath, placeDir);
		sceneObject.SoundVolume = Clamp01((p.SoundVolume <= 0f) ? 0.5f : p.SoundVolume);
		sceneObject.SoundLooped = p.SoundLooped;
		sceneObject.SoundPlaying = p.SoundPlaying;
		sceneObject.Script = p.Script ?? "";
		sceneObject.SoundPlayOnTouch = p.SoundPlayOnTouch;
		sceneObject.MeshPath = ResolveAsset(string.IsNullOrWhiteSpace(p.MeshPath) ? null : p.MeshPath, placeDir);
		sceneObject.CollisionFidelity = (Enum.IsDefined(p.CollisionFidelity) ? p.CollisionFidelity : CollisionFidelityKind.Box);
		sceneObject.RenderFidelity = ((!Enum.IsDefined(p.RenderFidelity)) ? RenderFidelityKind.Normal : p.RenderFidelity);
		SceneObject sceneObject3 = sceneObject;
		sceneObject3.ComposeOrientation();
		foreach (float[] item in p.Waypoints ?? new List<float[]>())
		{
			if (item.Length == 3 && float.IsFinite(item[0] + item[1] + item[2]))
			{
				if (sceneObject3.Waypoints.Count >= 16)
				{
					break;
				}
				sceneObject3.Waypoints.Add(new OpenTK.Mathematics.Vector3(Math.Clamp(item[0], -10000f, 10000f), Math.Clamp(item[1], -10000f, 10000f), Math.Clamp(item[2], -10000f, 10000f)));
			}
		}
		foreach (DecalDto item2 in p.Decals ?? new List<DecalDto>())
		{
			if (!string.IsNullOrWhiteSpace(item2.Image))
			{
				sceneObject3.Decals.Add(new DecalLayer
				{
					Image = (ResolveAsset(item2.Image, placeDir) ?? ""),
					Face = item2.Face,
					Transparency = Clamp01(item2.Transparency),
					Blur = Clamp01(item2.Blur),
					OffsetX = Math.Clamp(item2.OffsetX, -1f, 1f),
					OffsetY = Math.Clamp(item2.OffsetY, -1f, 1f),
					Scale = Math.Clamp((item2.Scale <= 0f) ? 1f : item2.Scale, 0.05f, 1f)
				});
			}
		}
		foreach (TextureDto item3 in p.Textures ?? new List<TextureDto>())
		{
			if (!string.IsNullOrWhiteSpace(item3.Image))
			{
				sceneObject3.Textures.Add(new TextureLayer
				{
					Image = (ResolveAsset(item3.Image, placeDir) ?? ""),
					Face = item3.Face,
					Transparency = Clamp01(item3.Transparency),
					TilingX = Math.Clamp((item3.TilingX <= 0f) ? 1f : item3.TilingX, 0.1f, 32f),
					TilingY = Math.Clamp((item3.TilingY <= 0f) ? 1f : item3.TilingY, 0.1f, 32f),
					OffsetX = Math.Clamp(item3.OffsetX, -10f, 10f),
					OffsetY = Math.Clamp(item3.OffsetY, -10f, 10f)
				});
			}
		}
		foreach (TextDto item4 in p.Texts ?? new List<TextDto>())
		{
			sceneObject3.Texts.Add(new TextLayer
			{
				Text = (string.IsNullOrEmpty(item4.Text) ? "Text" : ((item4.Text.Length > 200) ? item4.Text.Substring(0, 200) : item4.Text)),
				Face = item4.Face,
				Font = TextFonts.OrFallback(item4.Font),
				Size = Math.Clamp((item4.Size <= 0f) ? 48f : item4.Size, 8f, 256f),
				Color = ((PartColor.TryParseRgb(item4.Color, out var color) || PartColor.TryParseHex(item4.Color, out color)) ? item4.Color : "255, 255, 255"),
				Transparency = Clamp01(item4.Transparency),
				Blur = Clamp01(item4.Blur),
				OffsetX = Math.Clamp(item4.OffsetX, -1f, 1f),
				OffsetY = Math.Clamp(item4.OffsetY, -1f, 1f),
				Scale = Math.Clamp((item4.Scale <= 0f) ? 1f : item4.Scale, 0.05f, 1f),
				Bold = item4.Bold,
				Outline = Math.Clamp(item4.Outline, 0f, 16f),
				OutlineColor = item4.OutlineColor
			});
		}
		return sceneObject3;
	}

	private SceneObject DtoToLight(LightDto l)
	{
		LightKind lightKind = ((l.Kind == LightKind.Spot) ? LightKind.Point : l.Kind);
		if (lightKind != LightKind.Point)
		{
			throw new InvalidDataException("unsupported light kind");
		}
		return new SceneObject
		{
			Name = (string.IsNullOrWhiteSpace(l.Name) ? "PointLight" : l.Name),
			Shape = ShapeKind.None,
			Light = lightKind,
			Position = F3(l.Position, new OpenTK.Mathematics.Vector3(0f, 5f, 0f)),
			Size = new OpenTK.Mathematics.Vector3(1f, 1f, 1f),
			Color = new Color4(Clamp01(F3(l.Color, new OpenTK.Mathematics.Vector3(1f, 1f, 1f)).X), Clamp01(F3(l.Color, new OpenTK.Mathematics.Vector3(1f, 1f, 1f)).Y), Clamp01(F3(l.Color, new OpenTK.Mathematics.Vector3(1f, 1f, 1f)).Z), 1f),
			Brightness = Math.Clamp(l.Brightness, 0f, 20f),
			Range = Math.Clamp((l.Range <= 0f) ? 16f : l.Range, 1f, 100f),
			Anchored = true
		};
	}

	private PlaceFile CapturePlace(string? relativizeDir = null)
	{
		PlaceFile placeFile = new PlaceFile
		{
			Sun = new SunDto
			{
				Direction = V3(_scene.SunDirection),
				Color = V3(_scene.SunColor),
				Intensity = _scene.SunIntensity,
				Shadows = _scene.ShadowsEnabled
			},
			Atmosphere = new AtmosphereDto
			{
				FogDensity = _scene.FogDensity,
				FogColor = V3(_scene.FogColor),
				Haze = _scene.SkyHaze,
				SkyTint = V3(_scene.SkyTint),
				StarAmount = _scene.StarAmount,
				CloudAmount = _scene.CloudAmount,
				SunSize = _scene.SunSize,
				SunStripes = _scene.SunStripes,
				SkyStyle = _scene.SkyStyle
			},
			Ambient = _scene.AmbientBoost,
			TimeOfDay = _scene.TimeOfDay,
			SkyPreset = _skyPresetName,
			Camera = new CameraDto
			{
				Position = V3(_scene.Position),
				Yaw = _scene.Yaw,
				Pitch = _scene.Pitch
			}
		};
		foreach (SceneObject @object in _scene.Objects)
		{
			if (@object != _physics.Player && !@object.IsAvatarFace)
			{
				if (@object.Shape == ShapeKind.None && @object.Light != LightKind.None)
				{
					placeFile.Lights.Add(CaptureLight(@object));
				}
				else
				{
					placeFile.Parts.Add(CapturePart(@object, relativizeDir));
				}
			}
		}
		return placeFile;
	}

	private bool WritePlace(string path)
	{
		try
		{
			string relativizeDir = null;
			try
			{
				relativizeDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
			}
			catch
			{
			}
			File.WriteAllText(path, JsonSerializer.Serialize(CapturePlace(relativizeDir), PlaceJson));
			_savePath = path;
			_dirty = false;
			UpdateTitle();
			NoteRecent(path);
			Log($"Saved {path} ({_scene.Objects.Count} parts).");
			StatusText.Text = "Saved " + System.IO.Path.GetFileName(path);
			return true;
		}
		catch (Exception ex)
		{
			Log("Save failed: " + ex.Message);
			StatusText.Text = "Save failed";
			return false;
		}
	}

	public bool SavePlace()
	{
		if (_savePath == null)
		{
			return SavePlaceAs();
		}
		return WritePlace(_savePath);
	}

	private bool SavePlaceAs()
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Save place",
			Filter = "Builder Place (*.bp)|*.bp|All files|*.*",
			DefaultExt = ".bp",
			AddExtension = true,
			FileName = ((_savePath != null) ? System.IO.Path.GetFileName(_savePath) : "Untitled.bp"),
			OverwritePrompt = true
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return false;
		}
		return WritePlace(saveFileDialog.FileName);
	}

	private async void ExportGame_Click(object sender, RoutedEventArgs e)
	{
		CloseBackstage();
		await ExportGameAssets();
	}

	private async Task ExportGameAssets()
	{
		if ((_savePath == null || _dirty) && !SavePlace())
		{
			Log("Export cancelled (save the place first).");
		}
		else
		{
			if (_savePath == null)
			{
				return;
			}
			string savePath = _savePath;
			if (!TryReadPlaceFile(savePath, out PlaceFile file) || file == null)
			{
				return;
			}
			string gameName = SafeGameName(System.IO.Path.GetFileNameWithoutExtension(savePath));
			string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Builder Games", gameName);
			if (Directory.Exists(folder))
			{
				if (new ConfirmWindow
				{
					Owner = this,
					Message = "\"" + gameName + "\" already exists. Replace it?"
				}.ShowDialog() != true)
				{
					Log("Export cancelled.");
					return;
				}
				try
				{
					Directory.Delete(folder, recursive: true);
				}
				catch (Exception ex)
				{
					Log("Export failed: can't clear folder: " + ex.Message);
					StatusText.Text = "Export failed";
					return;
				}
			}
			try
			{
				string placeDir;
				try
				{
					placeDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(savePath)) ?? "";
				}
				catch
				{
					placeDir = "";
				}
				if (!WriteBundledPlace(file, placeDir, folder))
				{
					return;
				}
				StatusText.Text = "Copying engine...";
				string engineDir = AppContext.BaseDirectory;
				await Task.Run(delegate
				{
					CopyEngineFiles(engineDir, folder);
				});
				try
				{
					string text = System.IO.Path.Combine(folder, "AppIcons", "publishedIcon.ico");
					Directory.CreateDirectory(System.IO.Path.GetDirectoryName(text));
					if (File.Exists(PublishedIconPath))
					{
						File.Copy(PublishedIconPath, text, overwrite: true);
					}
				}
				catch (Exception ex2)
				{
					Log("Game icon copy failed: " + ex2.Message);
				}
				string value = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
				File.WriteAllText(System.IO.Path.Combine(folder, "player.mode"), $"{gameName}\nengine {value}\nportable export {DateTime.Now:G}\n");
				double value2 = DirSizeMb(folder);
				Log($"Portable game ready: {folder} ({value2:0} MB). Run it with builder.exe inside.");
				StatusText.Text = $"Exported {gameName} ({value2:0} MB)";
			}
			catch (Exception ex3)
			{
				Log("Export failed: " + ex3.Message);
				StatusText.Text = "Export failed";
				return;
			}
			OfferGameShortcut(folder, gameName);
			InfoWindow infoWindow = new InfoWindow();
			infoWindow.Owner = this;
			infoWindow.Message = "Saving to exe is done!\n\nRun it here:\n" + System.IO.Path.Combine(folder, "builder.exe");
			infoWindow.ShowDialog();
		}
	}

	private bool WriteBundledPlace(PlaceFile place, string placeDir, string folder)
	{
		try
		{
			string assetsDir = System.IO.Path.Combine(folder, "Assets");
			Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			int bundled = 0;
			int missing = 0;
			foreach (PartDto item in place.Parts ?? new List<PartDto>())
			{
				item.DecalImage = BundleImage(item.DecalImage, placeDir, assetsDir, seen, ref bundled, ref missing);
				foreach (DecalDto item2 in item.Decals ?? new List<DecalDto>())
				{
					item2.Image = BundleImage(item2.Image, placeDir, assetsDir, seen, ref bundled, ref missing) ?? item2.Image;
				}
				foreach (TextureDto item3 in item.Textures ?? new List<TextureDto>())
				{
					item3.Image = BundleImage(item3.Image, placeDir, assetsDir, seen, ref bundled, ref missing) ?? item3.Image;
				}
				item.NpcFaceImage = BundleImage(item.NpcFaceImage, placeDir, assetsDir, seen, ref bundled, ref missing);
				item.SoundPath = BundleAudio(item.SoundPath, placeDir, assetsDir, seen, ref bundled, ref missing);
				BundleMeshSidecars(item.MeshPath, placeDir, assetsDir, seen, ref bundled, ref missing);
				item.MeshPath = BundleImage(item.MeshPath, placeDir, assetsDir, seen, ref bundled, ref missing);
			}
			Directory.CreateDirectory(folder);
			File.WriteAllText(System.IO.Path.Combine(folder, "game.bp"), JsonSerializer.Serialize(place, PlaceJson));
			double value = DirSizeMb(folder) * 1024.0;
			Log($"Game assets saved to {folder} ({place.Parts?.Count ?? 0} parts, {bundled} files, {value:0} KB).");
			if (missing > 0)
			{
				Log($"Note: {missing} referenced file(s) were missing on disk and kept as-is.");
			}
			return true;
		}
		catch (Exception ex)
		{
			Log("Export failed: " + ex.Message);
			StatusText.Text = "Export failed";
			return false;
		}
	}

	private static void CopyEngineFiles(string sourceDir, string destDir)
	{
		string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
		foreach (string text in files)
		{
			string fileName = System.IO.Path.GetFileName(text);
			if (!fileName.Equals("game.bp", StringComparison.OrdinalIgnoreCase) && !fileName.Equals("player.mode", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
			{
				string text2 = System.IO.Path.Combine(destDir, System.IO.Path.GetRelativePath(sourceDir, text));
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(text2));
				File.Copy(text, text2, overwrite: true);
			}
		}
	}

	private static string? BundleImage(string? path, string placeDir, string assetsDir, Dictionary<string, string> seen, ref int bundled, ref int missing)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		string path2 = path;
		if (!File.Exists(path2) && !System.IO.Path.IsPathRooted(path2) && placeDir.Length > 0)
		{
			path2 = System.IO.Path.Combine(placeDir, path);
		}
		if (!File.Exists(path2))
		{
			missing++;
			return path;
		}
		string fullPath = System.IO.Path.GetFullPath(path2);
		if (seen.TryGetValue(fullPath, out string value))
		{
			return value;
		}
		Directory.CreateDirectory(assetsDir);
		string fileName = System.IO.Path.GetFileName(fullPath);
		string text = System.IO.Path.Combine(assetsDir, fileName);
		if (!fullPath.Equals(System.IO.Path.GetFullPath(text), StringComparison.OrdinalIgnoreCase))
		{
			string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);
			string extension = System.IO.Path.GetExtension(fileName);
			int num = 2;
			while (File.Exists(text))
			{
				text = System.IO.Path.Combine(assetsDir, $"{fileNameWithoutExtension}_{num}{extension}");
				num++;
			}
			File.Copy(fullPath, text);
		}
		value = (seen[fullPath] = "Assets/" + System.IO.Path.GetFileName(text));
		bundled++;
		return value;
	}

	private static string? BundleAudio(string? path, string placeDir, string assetsDir, Dictionary<string, string> seen, ref int bundled, ref int missing)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		if (!AudioExtensions.Contains(System.IO.Path.GetExtension(path)))
		{
			return path;
		}
		return BundleImage(path, placeDir, assetsDir, seen, ref bundled, ref missing);
	}

	private static void BundleMeshSidecars(string? meshPath, string placeDir, string assetsDir, Dictionary<string, string> seen, ref int bundled, ref int missing)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(meshPath))
			{
				return;
			}
			string path = meshPath;
			if (!File.Exists(path) && !System.IO.Path.IsPathRooted(path) && placeDir.Length > 0)
			{
				path = System.IO.Path.Combine(placeDir, meshPath);
			}
			if (!File.Exists(path) || !System.IO.Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			string directoryName = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
			if (directoryName == null)
			{
				return;
			}
			Directory.CreateDirectory(assetsDir);
			foreach (string item in File.ReadLines(path))
			{
				string text = item.Trim();
				if (!text.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string[] array = text.Substring("mtllib ".Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
				foreach (string text2 in array)
				{
					string text3 = System.IO.Path.Combine(directoryName, text2.Trim());
					if (!File.Exists(text3))
					{
						missing++;
						continue;
					}
					string text4 = System.IO.Path.Combine(assetsDir, System.IO.Path.GetFileName(text3));
					if (!File.Exists(text4))
					{
						File.Copy(text3, text4);
						bundled++;
					}
					seen[System.IO.Path.GetFullPath(text3)] = "Assets/" + System.IO.Path.GetFileName(text4);
				}
			}
		}
		catch
		{
		}
	}

	private static string SafeGameName(string raw)
	{
		char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			raw = raw.Replace(oldChar, '_');
		}
		raw = raw.Trim();
		if (raw.Length > 40)
		{
			raw = raw.Substring(0, 40).Trim();
		}
		if (!string.IsNullOrEmpty(raw))
		{
			return raw;
		}
		return "MyGame";
	}

	private static double DirSizeMb(string folder)
	{
		double num = 0.0;
		string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
		foreach (string fileName in files)
		{
			try
			{
				num += (double)new FileInfo(fileName).Length;
			}
			catch
			{
			}
		}
		return num / 1048576.0;
	}

	private void OfferGameShortcut(string folder, string gameName)
	{
		if (new ConfirmWindow
		{
			Owner = this,
			Message = "Would you like to create a shortcut for the game?"
		}.ShowDialog() != true)
		{
			Log("Skipped desktop shortcut.");
			return;
		}
		try
		{
			string text = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "builder.exe");
			dynamic val = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
			string text2 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), gameName + ".lnk");
			dynamic val2 = val.CreateShortcut(text2);
			val2.TargetPath = text;
			val2.Arguments = "--play \"" + System.IO.Path.Combine(folder, "game.bp") + "\"";
			val2.WorkingDirectory = folder;
			val2.Description = gameName;
			string text3 = System.IO.Path.Combine(folder, "AppIcons", "publishedIcon.ico");
			if (File.Exists(text3))
			{
				val2.IconLocation = text3 + ",0";
			}
			val2.Save();
			Log("Desktop shortcut created: " + text2);
			StatusText.Text = "Exported " + gameName + " + shortcut";
		}
		catch (Exception ex)
		{
			Log("Shortcut failed: " + ex.Message);
			StatusText.Text = "Shortcut failed";
		}
	}

	private void LoadPlace(PlaceFile file, string path)
	{
		string placeDir = null;
		try
		{
			placeDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
		}
		catch
		{
		}
		_scene.Objects.Clear();
		foreach (PartDto item2 in file.Parts ?? new List<PartDto>())
		{
			SceneObject item = DtoToPart(item2, placeDir);
			_scene.Objects.Add(item);
		}
		foreach (LightDto item3 in file.Lights ?? new List<LightDto>())
		{
			try
			{
				_scene.Objects.Add(DtoToLight(item3));
			}
			catch
			{
			}
		}
		_scene.SunDirection = F3(file.Sun?.Direction, new OpenTK.Mathematics.Vector3(0.5f, 0.8f, 0.6f));
		TimeSlider.Value = Math.Clamp(file.TimeOfDay, 0f, 24f);
		_scene.SunColor = F3(file.Sun?.Color, _scene.SunColor);
		_scene.SunIntensity = Math.Clamp(file.Sun?.Intensity ?? 1f, 0f, 2f);
		ApplyShadows(file.Sun?.Shadows ?? true);
		_scene.AmbientBoost = Math.Clamp(file.Ambient, 0f, 2f);
		ApplyFog(Math.Clamp(file.Atmosphere?.FogDensity ?? 0.012f, 0f, 0.05f));
		_scene.FogColor = F3(file.Atmosphere?.FogColor, new OpenTK.Mathematics.Vector3(0.55f, 0.62f, 0.72f));
		_scene.SkyHaze = Math.Clamp(file.Atmosphere?.Haze ?? 0.012f, 0f, 0.05f);
		_scene.SkyTint = F3(file.Atmosphere?.SkyTint, new OpenTK.Mathematics.Vector3(1f, 1f, 1f));
		_scene.StarAmount = Math.Clamp(file.Atmosphere?.StarAmount ?? 1f, 0f, 2.5f);
		_scene.CloudAmount = Math.Clamp(file.Atmosphere?.CloudAmount ?? 1f, 0f, 2f);
		_scene.SunSize = Math.Clamp(file.Atmosphere?.SunSize ?? 1f, 0.5f, 3f);
		_scene.SunStripes = file.Atmosphere?.SunStripes ?? false;
		_scene.SkyStyle = Math.Clamp(file.Atmosphere?.SkyStyle ?? 0, 0, 2);
		_skyPresetName = ((SkyPreset.ByName(file.SkyPreset) != null) ? file.SkyPreset : "Custom");
		if (_skyPresetName == "Clear Blue" && _scene.SkyStyle == 0)
		{
			_scene.SkyStyle = 2;
		}
		_scene.Position = F3(file.Camera?.Position, new OpenTK.Mathematics.Vector3(0f, 1.5f, 8f));
		_scene.Yaw = file.Camera?.Yaw ?? (-90f);
		_scene.Pitch = file.Camera?.Pitch ?? (-10f);
		_partCounter = _scene.Objects.Count;
		_savePath = path;
		_dirty = false;
		NoteRecent(path);
		_undo.Clear();
		_redo.Clear();
		_lastUndoKey = null;
		UpdateTitle();
		RefreshExplorer();
		SelectObject(null);
		_scene.InvalidateShadows();
		_needsFrame = true;
		Log($"Opened {path} ({_scene.Objects.Count} parts).");
		StatusText.Text = "Opened " + System.IO.Path.GetFileName(path);
	}

	private void OpenPlace()
	{
		if (_physics.Simulating)
		{
			StopPlaying();
		}
		if (ConfirmDiscard("open a place"))
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Open place",
				Filter = "Builder Place (*.bp)|*.bp|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(this) == true && TryReadPlaceFile(openFileDialog.FileName, out PlaceFile file) && file != null)
			{
				LoadPlace(file, openFileDialog.FileName);
			}
		}
	}

	private void BuildTemplateList()
	{
		TemplateList.Children.Clear();
		foreach (PlaceTemplate item in Templates.All)
		{
			Border border = new Border
			{
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70)),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(10.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			DockPanel dockPanel = new DockPanel
			{
				LastChildFill = true
			};
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Content = "Create",
				MinWidth = 76.0,
				Padding = new Thickness(0.0, 6.0, 0.0, 6.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126, 34)),
				Foreground = System.Windows.Media.Brushes.White,
				BorderThickness = new Thickness(0.0),
				VerticalAlignment = VerticalAlignment.Center,
				Tag = item.Name,
				Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
			};
			button.Click += CreateTemplate_Click;
			DockPanel.SetDock(button, Dock.Right);
			dockPanel.Children.Add(button);
			StackPanel stackPanel = new StackPanel();
			stackPanel.Children.Add(new TextBlock
			{
				Text = item.Glyph + "  " + item.Name,
				Foreground = System.Windows.Media.Brushes.White,
				FontWeight = FontWeights.SemiBold,
				FontSize = 13.0
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = item.Description,
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			});
			dockPanel.Children.Add(stackPanel);
			border.Child = dockPanel;
			TemplateList.Children.Add(border);
		}
	}

	private void CreateTemplate_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string tag })
		{
			PlaceTemplate placeTemplate = Templates.ByName(tag);
			if (!(placeTemplate == null))
			{
				LoadTemplate(placeTemplate);
				CloseBackstage();
			}
		}
	}

	private void LoadTemplate(PlaceTemplate t)
	{
		if (_physics.Simulating)
		{
			StopPlaying();
		}
		if (!ConfirmDiscard("start the \"" + t.Name + "\" template"))
		{
			return;
		}
		TemplateScene templateScene = t.Build();
		_scene.Objects.Clear();
		foreach (SceneObject @object in templateScene.Objects)
		{
			_scene.Objects.Add(@object);
		}
		OpenTK.Mathematics.Vector3? spawnAt = templateScene.SpawnAt;
		if (spawnAt.HasValue)
		{
			OpenTK.Mathematics.Vector3 valueOrDefault = spawnAt.GetValueOrDefault();
			EnsureSpawn(valueOrDefault);
		}
		TimeSlider.Value = Math.Clamp(templateScene.TimeOfDay, 0f, 24f);
		_scene.Position = templateScene.Camera;
		_scene.Yaw = templateScene.Yaw;
		_scene.Pitch = templateScene.Pitch;
		_partCounter = _scene.Objects.Count;
		_savePath = null;
		_dirty = false;
		_undo.Clear();
		_redo.Clear();
		_lastUndoKey = null;
		UpdateTitle();
		RefreshExplorer();
		SelectObject(null);
		_scene.Contrast = AppSettings.Current.Contrast;
		_scene.InvalidateShadows();
		_needsFrame = true;
		Log($"Template \"{t.Name}\" ready ({_scene.Objects.Count} parts). Press Play!");
		StatusText.Text = "Template: " + t.Name;
	}

	private void NewPlace()
	{
		if (_physics.Simulating)
		{
			StopPlaying();
		}
		if (!ConfirmDiscard("create a new place"))
		{
			return;
		}
		_scene.Objects.Clear();
		TemplateScene templateScene = Templates.Baseplate();
		foreach (SceneObject @object in templateScene.Objects)
		{
			_scene.Objects.Add(@object);
		}
		OpenTK.Mathematics.Vector3? spawnAt = templateScene.SpawnAt;
		if (spawnAt.HasValue)
		{
			OpenTK.Mathematics.Vector3 valueOrDefault = spawnAt.GetValueOrDefault();
			EnsureSpawn(valueOrDefault);
		}
		_partCounter = _scene.Objects.Count;
		_scene.ResetCamera();
		_savePath = null;
		_dirty = false;
		_undo.Clear();
		_redo.Clear();
		_lastUndoKey = null;
		UpdateTitle();
		RefreshExplorer();
		SelectObject(null);
		_scene.InvalidateShadows();
		_needsFrame = true;
		Log("New place (baseplate + spawn).");
		StatusText.Text = "New place";
	}

	private bool ConfirmDiscard(string action)
	{
		if (!_dirty)
		{
			return true;
		}
		bool valueOrDefault = new ConfirmWindow
		{
			Owner = this,
			Message = "Discard unsaved changes and " + action + "? (Ctrl+S keeps them)"
		}.ShowDialog() == true;
		if (!valueOrDefault)
		{
			Log("Kept current place (Ctrl+S to save).");
		}
		return valueOrDefault;
	}

	private void CloseBackstage()
	{
		if (MainRibbon.Menu is Backstage backstage)
		{
			backstage.IsOpen = false;
		}
	}

	private void ShowStartPage()
	{
		BuildStartPage();
		GettingStartedView.Visibility = Visibility.Visible;
	}

	private void ShowGame()
	{
		GettingStartedView.Visibility = Visibility.Collapsed;
	}

	private void StartNew_Click(object sender, RoutedEventArgs e)
	{
		bool dirty = _dirty;
		NewPlace();
		if (!dirty || !_dirty)
		{
			ShowGame();
		}
	}

	private void StartOpen_Click(object sender, RoutedEventArgs e)
	{
		bool dirty = _dirty;
		OpenPlace();
		if (!dirty || !_dirty)
		{
			ShowGame();
		}
	}

	private void StartTemplate_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: string tag }))
		{
			return;
		}
		PlaceTemplate placeTemplate = Templates.ByName(tag);
		if (!(placeTemplate == null))
		{
			bool dirty = _dirty;
			LoadTemplate(placeTemplate);
			if (!dirty || !_dirty)
			{
				ShowGame();
			}
		}
	}

	private static string TemplateThumbPath(string name)
	{
		char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			name = name.Replace(oldChar, '_');
		}
		return System.IO.Path.Combine(AppContext.BaseDirectory, "Templates", name.Trim() + ".png");
	}

	private void BuildStartPage()
	{
		StartTemplateCards.Children.Clear();
		foreach (PlaceTemplate item in Templates.All)
		{
			StartTemplateCards.Children.Add(StartCard(item));
		}
		BuildRecentList();
	}

	private System.Windows.Controls.Button StartCard(PlaceTemplate t)
	{
		System.Windows.Controls.Image image = new System.Windows.Controls.Image
		{
			Height = 110.0,
			Stretch = Stretch.UniformToFill,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Shapes.Rectangle rectangle = new System.Windows.Shapes.Rectangle
		{
			Width = 64.0,
			Height = 64.0,
			Fill = System.Windows.Media.Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			OpacityMask = new ImageBrush(RenderGlyphMask(t.Glyph))
		};
		bool flag = false;
		try
		{
			string text = TemplateThumbPath(t.Name);
			if (File.Exists(text))
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.UriSource = new Uri(text);
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				image.Source = bitmapImage;
				flag = true;
			}
		}
		catch
		{
		}
		image.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		rectangle.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		Border element = new Border
		{
			Height = 110.0,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
			CornerRadius = new CornerRadius(6.0, 6.0, 0.0, 0.0),
			Child = new Grid
			{
				Children = 
				{
					(UIElement)image,
					(UIElement)rectangle
				}
			}
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(element);
		stackPanel.Children.Add(new TextBlock
		{
			Text = t.Name,
			Foreground = System.Windows.Media.Brushes.White,
			FontWeight = FontWeights.SemiBold,
			FontSize = 13.0,
			Margin = new Thickness(10.0, 8.0, 10.0, 0.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = t.Description,
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(10.0, 2.0, 10.0, 10.0),
			MaxHeight = 44.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			FontSize = 12.0
		});
		Border content = new Border
		{
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(6.0),
			Child = stackPanel,
			Width = 220.0
		};
		System.Windows.Controls.Button button = new System.Windows.Controls.Button();
		button.Content = content;
		button.Background = System.Windows.Media.Brushes.Transparent;
		button.BorderThickness = new Thickness(0.0);
		button.Padding = new Thickness(0.0);
		button.Margin = new Thickness(0.0, 0.0, 14.0, 14.0);
		button.Cursor = Cursors.Hand;
		button.Tag = t.Name;
		button.ToolTip = "Start from " + t.Name;
		button.Style = (Style)FindResource("StartCardButton");
		button.Click += StartTemplate_Click;
		return button;
	}

	private static ImageSource RenderGlyphMask(string glyph)
	{
		TextBlock textBlock = new TextBlock
		{
			Text = glyph,
			FontSize = 96.0,
			Foreground = System.Windows.Media.Brushes.Black,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		textBlock.Measure(new System.Windows.Size(128.0, 128.0));
		textBlock.Arrange(new Rect(new System.Windows.Size(128.0, 128.0)));
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(128, 128, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(textBlock);
		renderTargetBitmap.Freeze();
		return renderTargetBitmap;
	}

	private void BuildRecentList()
	{
		StartRecentList.Children.Clear();
		AppSettings current = AppSettings.Current;
		current.RecentFiles.RemoveAll(string.IsNullOrWhiteSpace);
		current.RecentFiles.RemoveAll((string p) => !File.Exists(p));
		if (current.RecentFiles.Count == 0)
		{
			StartRecentList.Children.Add(new TextBlock
			{
				Text = "No recent places yet \u2014 save something!",
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136))
			});
			return;
		}
		foreach (string recentFile in current.RecentFiles)
		{
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Content = System.IO.Path.GetFileName(recentFile),
				Background = System.Windows.Media.Brushes.Transparent,
				BorderThickness = new Thickness(0.0),
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126, 34)),
				Padding = new Thickness(0.0, 3.0, 0.0, 3.0),
				FontSize = 13.0,
				Cursor = Cursors.Hand,
				Tag = recentFile,
				Style = (Style)FindResource("StartCardButton")
			};
			button.Click += StartRecent_Click;
			stackPanel.Children.Add(button);
			stackPanel.Children.Add(new TextBlock
			{
				Text = "  \u2014  " + System.IO.Path.GetDirectoryName(recentFile),
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136)),
				VerticalAlignment = VerticalAlignment.Center,
				FontSize = 12.0
			});
			StartRecentList.Children.Add(stackPanel);
		}
	}

	private void StartRecent_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: var tag }))
		{
			return;
		}
		string path = tag as string;
		if (path == null)
		{
			return;
		}
		PlaceFile file;
		if (!File.Exists(path))
		{
			AppSettings.Current.RecentFiles.RemoveAll((string p) => p.Equals(path, StringComparison.OrdinalIgnoreCase));
			AppSettings.Current.Save();
			BuildRecentList();
			StatusText.Text = "Recent file is gone";
		}
		else if (ConfirmDiscard("open a place") && TryReadPlaceFile(path, out file) && file != null)
		{
			LoadPlace(file, path);
			ShowGame();
		}
	}

	private void NoteRecent(string path)
	{
		try
		{
			AppSettings current = AppSettings.Current;
			current.RecentFiles.RemoveAll((string p) => p.Equals(path, StringComparison.OrdinalIgnoreCase));
			current.RecentFiles.Insert(0, path);
			while (current.RecentFiles.Count > 8)
			{
				current.RecentFiles.RemoveAt(current.RecentFiles.Count - 1);
			}
			current.Save();
		}
		catch
		{
		}
	}

	private void New_Click(object sender, RoutedEventArgs e)
	{
		NewPlace();
		CloseBackstage();
	}

	private void StartPage_Click(object sender, RoutedEventArgs e)
	{
		CloseBackstage();
		ShowStartPage();
	}

	private void Open_Click(object sender, RoutedEventArgs e)
	{
		OpenPlace();
		CloseBackstage();
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		if (SavePlace())
		{
			CloseBackstage();
		}
	}

	private void SaveAs_Click(object sender, RoutedEventArgs e)
	{
		if (SavePlaceAs())
		{
			CloseBackstage();
		}
	}

	private void ToggleExplorer_Click(object sender, RoutedEventArgs e)
	{
		bool flag = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		ExplorerPanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		SyncMergedColumn();
	}

	private void ToggleToolbox_Click(object sender, RoutedEventArgs e)
	{
		Fluent.ToggleButton obj = sender as Fluent.ToggleButton;
		if (obj != null && obj.IsChecked == true)
		{
			ToolboxRow.Height = _toolboxRowH;
			ToolboxRow.MinHeight = 150.0;
			ToolboxPanel.Visibility = Visibility.Visible;
		}
		else
		{
			_toolboxRowH = ToolboxRow.Height;
			ToolboxRow.Height = new GridLength(0.0);
			ToolboxRow.MinHeight = 0.0;
			ToolboxPanel.Visibility = Visibility.Collapsed;
		}
		SyncToolboxSplits();
		SyncMergedColumn();
	}

	private void SyncToolboxSplits()
	{
		bool flag = ToolboxPanel.Visibility == Visibility.Visible;
		bool flag2 = PropertiesPanel.Visibility == Visibility.Visible;
		SplitBRow.Height = ((flag && flag2) ? new GridLength(5.0) : new GridLength(0.0));
	}

	private void ToggleProperties_Click(object sender, RoutedEventArgs e)
	{
		Fluent.ToggleButton obj = sender as Fluent.ToggleButton;
		if (obj != null && obj.IsChecked == true)
		{
			PropsRow.Height = _propsRowH;
			PropsRow.MinHeight = 180.0;
			PropertiesPanel.Visibility = Visibility.Visible;
		}
		else
		{
			_propsRowH = PropsRow.Height;
			PropsRow.Height = new GridLength(0.0);
			PropsRow.MinHeight = 0.0;
			PropertiesPanel.Visibility = Visibility.Collapsed;
		}
		SyncToolboxSplits();
		SyncMergedColumn();
	}

	private void SyncMergedColumn()
	{
		bool flag = ExplorerPanel.Visibility == Visibility.Visible;
		if (flag)
		{
			if (LeftCol.Width.Value == 0.0)
			{
				LeftCol.Width = _leftWidth;
				LeftCol.MinWidth = 180.0;
			}
		}
		else
		{
			if (LeftCol.Width.Value != 0.0)
			{
				_leftWidth = LeftCol.Width;
			}
			LeftCol.Width = new GridLength(0.0);
			LeftCol.MinWidth = 0.0;
		}
		LeftSplitCol.Width = (flag ? new GridLength(5.0) : new GridLength(0.0));
		bool flag2 = ToolboxPanel.Visibility == Visibility.Visible || PropertiesPanel.Visibility == Visibility.Visible;
		if (flag2)
		{
			if (RightCol.Width.Value == 0.0)
			{
				RightCol.Width = _rightWidth;
				RightCol.MinWidth = 200.0;
			}
		}
		else
		{
			if (RightCol.Width.Value != 0.0)
			{
				_rightWidth = RightCol.Width;
			}
			RightCol.Width = new GridLength(0.0);
			RightCol.MinWidth = 0.0;
		}
		RightSplitCol.Width = (flag2 ? new GridLength(5.0) : new GridLength(0.0));
	}

	private void ToggleOutput_Click(object sender, RoutedEventArgs e)
	{
		bool flag = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		OutputPanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		OutputRow.Height = (flag ? new GridLength(170.0) : new GridLength(0.0));
	}

	private void VSync_Click(object sender, RoutedEventArgs e)
	{
		bool flag = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		ApplyVsync(flag);
		Log(flag ? $"VSync on: compositor-paced presentation (\u2248{_refreshHz:0} Hz)." : "VSync off: render on every compositor tick.");
	}

	private void Msaa_Click(object sender, RoutedEventArgs e)
	{
		bool flag = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		ApplyMsaa(flag);
		_needsFrame = true;
		Log("MSAA " + (flag ? "4x on" : "off") + ".");
	}

	private void Shadows_Click(object sender, RoutedEventArgs e)
	{
		bool flag = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		ApplyShadows(flag);
		Log("Shadows " + (flag ? "on" : "off") + ".");
	}

	private void Screenshot_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Save viewport screenshot",
			Filter = "PNG image|*.png",
			DefaultExt = ".png",
			AddExtension = true,
			FileName = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
			OverwritePrompt = true
		};
		if (saveFileDialog.ShowDialog(this) == true)
		{
			_scene.ScreenshotLog = Log;
			_scene.ScreenshotPath = saveFileDialog.FileName;
			_needsFrame = true;
			StatusText.Text = "Capturing screenshot...";
		}
	}

	private void ViewportControl_DragOver(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent("ToolboxItem") && e.Data.GetData("ToolboxItem") is string id)
		{
			e.Effects = DragDropEffects.Copy;
			System.Windows.Point position = e.GetPosition(ViewportControl);
			float w = (float)ViewportControl.ActualWidth;
			float h = (float)ViewportControl.ActualHeight;
			if (_scene.PickPoint((float)position.X, (float)position.Y, w, h, out var point))
			{
				ShowGhost(id, point);
			}
			else
			{
				ClearGhost();
			}
		}
		else
		{
			e.Effects = DragDropEffects.None;
		}
		e.Handled = true;
	}

	private void ViewportControl_DragLeave(object sender, DragEventArgs e)
	{
		ClearGhost();
	}

	private static (ShapeKind Shape, OpenTK.Mathematics.Vector3 Size)? GhostSpec(string id)
	{
		if (id.StartsWith("shape:") && Enum.TryParse<ShapeKind>(id.Substring(6), out var result))
		{
			return (result, new OpenTK.Mathematics.Vector3(1f, 1f, 1f));
		}
		switch (id)
		{
		case "plane":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(4f, 0.2f, 4f));
		case "mesh":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(1f, 1f, 1f));
		case "light":
			return (ShapeKind.Ball, new OpenTK.Mathematics.Vector3(1f, 1f, 1f));
		case "spawn":
		case "teleport":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(2f, 0.5f, 2f));
		case "bounce":
		case "checkpoint":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(3f, 0.4f, 3f));
		case "breakable":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(4f, 4f, 4f));
		case "water":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(8f, 3f, 8f));
		case "mover":
		case "timed":
		case "kill":
		case "conveyor":
		case "magnet":
		case "npc":
		case "spinner":
			return (ShapeKind.Block, new OpenTK.Mathematics.Vector3(2f, 2f, 2f));
		default:
			return null;
		}
	}

	private static float DropLift(string id)
	{
		bool flag;
		switch (id)
		{
		case "spawn":
		case "teleport":
		case "bounce":
		case "checkpoint":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			(ShapeKind, OpenTK.Mathematics.Vector3)? tuple = GhostSpec(id);
			if (!tuple.HasValue)
			{
				return 0.5f;
			}
			return tuple.GetValueOrDefault().Item2.Y * 0.5f;
		}
		return 0.3f;
	}

	private void ShowGhost(string id, OpenTK.Mathematics.Vector3 drop)
	{
		(ShapeKind, OpenTK.Mathematics.Vector3)? tuple = GhostSpec(id);
		if (!tuple.HasValue)
		{
			ClearGhost();
			return;
		}
		_scene.SetGhost(tuple.Value.Item1, SnapDropPoint(drop, DropLift(id)), tuple.Value.Item2, new OpenTK.Mathematics.Vector3(0.95f));
		_needsFrame = true;
	}

	public void ClearGhost()
	{
		_scene.ClearGhost();
		_needsFrame = true;
	}

	private void ViewportControl_Drop(object sender, DragEventArgs e)
	{
		if (!e.Data.GetDataPresent("ToolboxItem"))
		{
			return;
		}
		if (_physics.Simulating || _playerMode)
		{
			Log("Toolbox: stop playing to spawn parts.");
			StatusText.Text = "Stop playing first";
			e.Handled = true;
		}
		else
		{
			if (!(e.Data.GetData("ToolboxItem") is string text))
			{
				return;
			}
			System.Windows.Point position = e.GetPosition(ViewportControl);
			float w = (float)ViewportControl.ActualWidth;
			float h = (float)ViewportControl.ActualHeight;
			OpenTK.Mathematics.Vector3? drop = null;
			SceneObject sceneObject = null;
			string face = "Front";
			bool flag = ((text == "decal" || text == "text") ? true : false);
			OpenTK.Mathematics.Vector3 point2;
			if (flag || text.StartsWith("img:") || text.StartsWith("snd:"))
			{
				sceneObject = _scene.Pick((float)position.X, (float)position.Y, w, h);
				if ((text == "decal" || text.StartsWith("img:")) && sceneObject != null && _scene.PickSurface((float)position.X, (float)position.Y, w, h, new HashSet<SceneObject>(), out var _, out var normal))
				{
					face = FaceFromNormal(sceneObject, normal);
				}
			}
			else if (_scene.PickPoint((float)position.X, (float)position.Y, w, h, out point2))
			{
				drop = point2;
			}
			SpawnToolboxItem(text, drop, sceneObject, face);
			ClearGhost();
			e.Handled = true;
		}
	}

	private void ViewPreset_Click(object sender, RoutedEventArgs e)
	{
		string text = ((sender as Fluent.Button)?.Tag as string) ?? "Front";
		SceneObject selected = _scene.Selected;
		OpenTK.Mathematics.Vector3 target = selected?.Position ?? OpenTK.Mathematics.Vector3.Zero;
		float radius = ((selected != null) ? Math.Max(selected.Size.Length / 2f, 1f) : 8f);
		if (!(text == "Top"))
		{
			if (text == "Side")
			{
				_scene.Yaw = 0f;
				_scene.Pitch = 0f;
			}
			else
			{
				_scene.Yaw = -90f;
				_scene.Pitch = 0f;
			}
		}
		else
		{
			_scene.Yaw = -90f;
			_scene.Pitch = -89f;
		}
		_scene.FocusOn(target, radius);
		_needsFrame = true;
		Log($"View ? {text} {((selected != null) ? ("on " + selected.Name) : "on origin")}.");
		StatusText.Text = "View: " + text;
	}

	private void ThemeToggle_Click(object sender, RoutedEventArgs e)
	{
		bool flag = ((sender as Fluent.ToggleButton)?.IsChecked == true);
		SetTheme(flag);
		Log("Theme ? " + (flag ? "Dark.Cobalt" : "Light.Cobalt") + ".");
	}

	private void TimeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (TimeSlider != null && TimeLabel != null)
		{
			float num = (float)TimeSlider.Value;
			if (base.IsLoaded && !_applyingSky)
			{
				PushUndo("Time");
			}
			if (!_applyingSky && !_restoringHistory)
			{
				_skyPresetName = "Custom";
			}
			_scene.SetTimeOfDay(num);
			TimeLabel.Text = $"{(int)num:00}:{(int)(((double)num - Math.Floor(num)) * 60.0):00}";
			AppSettings.Current.TimeOfDay = num;
			AppSettings.Current.Save();
			UpdateLiveProps();
			MarkDirty();
			_needsFrame = true;
		}
	}

	public void ApplySkyPreset(string name)
	{
		SkyPreset skyPreset = SkyPreset.ByName(name);
		if (skyPreset == null)
		{
			return;
		}
		SceneSnapshot snap = CaptureState("Sky");
		_applyingSky = true;
		try
		{
			TimeSlider.Value = skyPreset.Time;
			if (skyPreset.SunColor.HasValue)
			{
				_scene.SunColor = skyPreset.SunColor.Value;
			}
			if (skyPreset.SunIntensity.HasValue)
			{
				_scene.SunIntensity = Math.Clamp(skyPreset.SunIntensity.Value, 0f, 2f);
			}
			if (skyPreset.Ambient.HasValue)
			{
				_scene.AmbientBoost = Math.Clamp(skyPreset.Ambient.Value, 0f, 2f);
			}
			_scene.SkyHaze = Math.Clamp(skyPreset.Haze, 0f, 0.05f);
			_scene.FogColor = skyPreset.FogColor;
			ApplyFog(Math.Clamp(skyPreset.FogDensity, 0f, 0.05f));
			_scene.SkyTint = skyPreset.Tint;
			_scene.StarAmount = skyPreset.Stars;
			_scene.CloudAmount = skyPreset.Clouds;
			_scene.SunSize = skyPreset.SunSize;
			_scene.SunStripes = skyPreset.Stripes;
			_scene.SkyStyle = skyPreset.Style;
			_skyPresetName = skyPreset.Name;
		}
		finally
		{
			_applyingSky = false;
		}
		PushCaptured(snap, mergeable: false);
		UpdateLiveProps();
		MarkDirty();
		_needsFrame = true;
		Log($"Sky ? {skyPreset.Name} ({skyPreset.Hint}) [style {skyPreset.Style}]");
		StatusText.Text = "Sky: " + skyPreset.Name;
	}

	public static void ApplyTextModeTo(Window w)
	{
		bool flag = AppSettings.Current.TextMode == "Smooth";
		bool flag2 = AppSettings.Current.TextMode == "Gray";
		TextOptions.SetTextFormattingMode(w, (!flag) ? TextFormattingMode.Display : TextFormattingMode.Ideal);
		TextOptions.SetTextRenderingMode(w, flag2 ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);
	}

	public void ApplyTextMode(string mode)
	{
		bool flag;
		switch (mode)
		{
		case "Sharp":
		case "Smooth":
		case "Gray":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return;
		}
		AppSettings.Current.TextMode = mode;
		AppSettings.Current.TextSharp = mode != "Smooth";
		AppSettings.Current.Save();
		foreach (Window window in Application.Current.Windows)
		{
			ApplyTextModeTo(window);
		}
	}

	public void ApplyAllSettings()
	{
		AppSettings current = AppSettings.Current;
		ApplyTextModeTo(this);
		ApplyCameraSettings();
		ApplyPlayerSettings();
		_scene.PulseStrength = current.SelectionGlow;
		_scene.Contrast = current.Contrast;
		_physics.Friction = current.Friction;
		SnapCheck.IsChecked = current.SnapEnabled;
		SnapBox.Text = current.SnapIncrement.ToString("0.#####", CultureInfo.InvariantCulture);
		_scene.FovDeg = current.Fov;
		_scene.FogDensity = current.FogDensity;
		_scene.SetTimeOfDay(current.TimeOfDay);
		_vsync = current.VSync;
		VSyncToggle.IsChecked = current.VSync;
		MsaaToggle.IsChecked = current.Msaa;
		ShadowsToggle.IsChecked = current.Shadows;
		_scene.ShadowsEnabled = current.Shadows;
		_scene.WaterReflections = current.WaterReflections;
		_scene.RequestShadowSize(current.ShadowSize);
		_scene.ShadowDistance = current.ShadowDistance;
		DarkToggle.IsChecked = current.DarkTheme;
		ApplyWindowMode(current.WindowMode, save: false);
	}

	public void ApplyCameraSettings()
	{
		_scene.LookSensitivity = AppSettings.Current.LookSensitivity;
		_scene.MoveSpeed = AppSettings.Current.MoveSpeed;
		_scene.LookResponsiveness = AppSettings.Current.LookSmoothing;
		_scene.MoveResponsiveness = AppSettings.Current.MoveSmoothing;
		_scene.InvertLookY = AppSettings.Current.InvertLookY;
	}

	public void ApplySelectionGlow(float glow)
	{
		glow = Math.Clamp(glow, 0f, 1f);
		_scene.PulseStrength = glow;
		AppSettings.Current.SelectionGlow = glow;
		AppSettings.Current.Save();
		_needsFrame = true;
	}

	public void ApplyContrast(float contrast)
	{
		contrast = Math.Clamp(contrast, 0.2f, 2f);
		_scene.Contrast = contrast;
		AppSettings.Current.Contrast = contrast;
		AppSettings.Current.Save();
		_needsFrame = true;
	}

	public void ApplySnapDefault(bool on)
	{
		SnapCheck.IsChecked = on;
		AppSettings.Current.SnapEnabled = on;
		AppSettings.Current.Save();
	}

	private void SnapCheck_Click(object sender, RoutedEventArgs e)
	{
		ApplySnapDefault(SnapCheck.IsChecked == true);
		Log("Snap " + ((SnapCheck.IsChecked == true) ? ("on (" + SnapStepText() + " stud)") : "off") + ".");
	}

	private float SnapIncrement()
	{
		if (SnapBox != null && float.TryParse(SnapBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result > 0f)
		{
			return Math.Min(result, 8f);
		}
		return AppSettings.Current.SnapIncrement;
	}

	private string SnapStepText()
	{
		return SnapIncrement().ToString("0.#####", CultureInfo.InvariantCulture);
	}

	private float SnapFloat(float v)
	{
		float num = SnapIncrement();
		return MathF.Round(v / num) * num;
	}

	private OpenTK.Mathematics.Vector3 SnapVec(OpenTK.Mathematics.Vector3 v)
	{
		return new OpenTK.Mathematics.Vector3(SnapFloat(v.X), SnapFloat(v.Y), SnapFloat(v.Z));
	}

	private void SnapBox_Commit(object sender, RoutedEventArgs e)
	{
		CommitSnapBox();
	}

	private void SnapBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			CommitSnapBox();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			SnapBox.Text = SnapStepText();
			ViewportControl.Focus();
			e.Handled = true;
		}
	}

	private void CommitSnapBox()
	{
		float snapIncrement = SnapIncrement();
		AppSettings.Current.SnapIncrement = snapIncrement;
		AppSettings.Current.Save();
		SnapBox.Text = SnapStepText();
		Log("Snap step: " + SnapStepText() + " stud.");
		StatusText.Text = "Snap step " + SnapStepText();
		_needsFrame = true;
	}

	public void ApplyFriction(float friction)
	{
		friction = Math.Clamp(friction, 0f, 2f);
		_physics.Friction = friction;
		AppSettings.Current.Friction = friction;
		AppSettings.Current.Save();
	}

	public void ApplyPlayerSettings()
	{
		_physics.WalkSpeed = AppSettings.Current.PlayerWalkSpeed;
		_physics.JumpSpeed = AppSettings.Current.PlayerJumpPower;
		_physics.CoyoteTime = AppSettings.Current.PlayerCoyote;
		if (_scene.FollowTarget != null)
		{
			_scene.FollowDistance = AppSettings.Current.PlayerZoom;
			_scene.ResetFollowDistance();
		}
	}

	public void ApplyAvatarColor()
	{
		AppSettings current = AppSettings.Current;
		if (_physics.Player != null)
		{
			_physics.Player.Color = new Color4(current.AvatarR, current.AvatarG, current.AvatarB, 1f);
			_needsFrame = true;
		}
	}

	public void ApplyFov(float fov)
	{
		_scene.FovDeg = fov;
		AppSettings.Current.Fov = fov;
		AppSettings.Current.Save();
	}

	public void ApplyFog(float density)
	{
		_scene.FogDensity = density;
		AppSettings.Current.FogDensity = density;
		AppSettings.Current.Save();
		MarkDirty();
	}

	public void RequestShadowSize(int size)
	{
		AppSettings current = AppSettings.Current;
		bool flag;
		switch (size)
		{
		case 256:
		case 512:
		case 1024:
		case 2048:
		case 4096:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		current.ShadowSize = (flag ? size : 1024);
		AppSettings.Current.Save();
		_scene.RequestShadowSize(AppSettings.Current.ShadowSize);
	}

	public void ApplyShadowDistance(float distance)
	{
		distance = Math.Clamp(distance, 10f, 120f);
		AppSettings.Current.ShadowDistance = distance;
		AppSettings.Current.Save();
		_scene.ShadowDistance = distance;
		_needsFrame = true;
	}

	public void SetTheme(bool dark)
	{
		AppSettings.Current.DarkTheme = dark;
		App.ApplyStudioTheme(Application.Current, dark);
		if (DarkToggle.IsChecked != dark)
		{
			DarkToggle.IsChecked = dark;
		}
		AppSettings.Current.Save();
	}

	public void ApplyVsync(bool on)
	{
		_vsync = on;
		AppSettings.Current.VSync = on;
		AppSettings.Current.Save();
	}

	public void ApplyMsaa(bool on)
	{
		AppSettings.Current.Msaa = on;
		if (_glStarted)
		{
			ViewportControl.Settings.Samples = (on ? 4 : 0);
		}
		AppSettings.Current.Save();
	}

	public void ApplyShadows(bool on)
	{
		AppSettings.Current.Shadows = on;
		if (ShadowsToggle.IsChecked != on)
		{
			ShadowsToggle.IsChecked = on;
		}
		_scene.ShadowsEnabled = on;
		_needsFrame = true;
		AppSettings.Current.Save();
	}

	public void ApplyWaterReflections(bool on)
	{
		AppSettings.Current.WaterReflections = on;
		_scene.WaterReflections = on;
		_needsFrame = true;
		AppSettings.Current.Save();
	}

	private void ApplyWindowIcon()
	{
		try
		{
			if (File.Exists(EngineIconPath))
			{
				base.Icon = new BitmapImage(new Uri(EngineIconPath));
			}
		}
		catch
		{
		}
	}

	public void ApplyWindowMode(string mode, bool save = true)
	{
		AppSettings.Current.WindowMode = mode;
		if (mode == "Borderless")
		{
			if (!_winBoundsSaved && base.WindowState == WindowState.Normal && !double.IsNaN(base.Left) && !double.IsNaN(base.Top))
			{
				_winBounds = new Rect(base.Left, base.Top, base.Width, base.Height);
				_winBoundsSaved = true;
			}
			base.WindowStyle = WindowStyle.None;
			base.ResizeMode = ResizeMode.NoResize;
			base.WindowState = WindowState.Maximized;
		}
		else
		{
			base.WindowState = WindowState.Normal;
			base.WindowStyle = WindowStyle.SingleBorderWindow;
			base.ResizeMode = ResizeMode.CanResize;
			if (_winBoundsSaved && !_winBounds.IsEmpty)
			{
				base.Left = _winBounds.X;
				base.Top = _winBounds.Y;
				base.Width = _winBounds.Width;
				base.Height = _winBounds.Height;
			}
		}
		if (save)
		{
			AppSettings.Current.Save();
		}
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		if (_settingsWin == null)
		{
			_settingsWin = new SettingsWindow(this);
			_settingsWin.Closed += delegate
			{
				_settingsWin = null;
			};
			_settingsWin.Show();
		}
		else
		{
			_settingsWin.Activate();
		}
		Log("Settings opened.");
	}

	private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if ((e.Key == Key.LeftAlt || e.Key == Key.RightAlt || (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))) && !_playerMode && (_tool == "Scale" || _dragAxis >= 0))
		{
			e.Handled = true;
			return;
		}
		if (_playerMode)
		{
			if (e.Key == Key.Escape && !(e.OriginalSource is System.Windows.Controls.TextBox))
			{
				TogglePause();
				e.Handled = true;
			}
			return;
		}
		bool flag = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
		bool flag2 = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift);
		if (flag && e.Key == Key.S)
		{
			SavePlace();
			e.Handled = true;
		}
		else if (flag && e.Key == Key.O)
		{
			OpenPlace();
			e.Handled = true;
		}
		else if (e.Key == Key.F2 && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			if (_scene.Selected == null)
			{
				StatusText.Text = "Nothing selected";
				Log("Rename: nothing selected.");
			}
			else
			{
				if (PropertiesPanel.Visibility != Visibility.Visible)
				{
					PropertiesToggle.IsChecked = true;
					ToggleProperties_Click(PropertiesToggle, e);
				}
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					InspectorName.Focus();
					InspectorName.SelectAll();
				});
				Log("Rename: " + _scene.Selected.Name);
			}
			e.Handled = true;
		}
		else if (flag && (e.Key == Key.Z || e.Key == Key.Y) && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			if (e.Key == Key.Z && !flag2)
			{
				Undo();
			}
			else
			{
				Redo();
			}
			e.Handled = true;
		}
		else if (flag && e.Key == Key.D && !e.IsRepeat && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			Duplicate_Click(sender, e);
			e.Handled = true;
		}
		else if (flag && e.Key == Key.C && !e.IsRepeat && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			Copy_Click(sender, e);
			e.Handled = true;
		}
		else if (flag && e.Key == Key.X && !e.IsRepeat && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			Cut_Click(sender, e);
			e.Handled = true;
		}
		else if (flag && e.Key == Key.V && !e.IsRepeat && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			Paste_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.R && !flag && !(e.OriginalSource is System.Windows.Controls.TextBox) && !_physics.Simulating)
		{
			RotateSelected90();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape && !(e.OriginalSource is System.Windows.Controls.TextBox))
		{
			if (_scene.SelWaypointObj != null)
			{
				_scene.SelWaypointObj = null;
				_scene.SelWaypointIndex = -1;
				_needsFrame = true;
				e.Handled = true;
			}
			else if (_marqueeActive)
			{
				CancelMarquee();
				e.Handled = true;
			}
			else if (_scene.Selected != null)
			{
				SelectObject(null);
				e.Handled = true;
			}
			else if (AppSettings.Current.IsBorderless)
			{
				ApplyWindowMode("Windowed");
				_settingsWin?.SyncWindowMode();
				Log("Back to windowed (Esc).");
				e.Handled = true;
			}
		}
	}

	private void BuildSwatches()
	{
		foreach (PartColor item in PartColor.Palette)
		{
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Width = 30.0,
				Height = 26.0,
				Margin = new Thickness(2.0),
				Background = new SolidColorBrush(item.ToMediaColor()),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102)),
				BorderThickness = new Thickness(1.0),
				ToolTip = item.Name,
				Tag = item
			};
			button.Click += Swatch_Click;
			SwatchGrid.Children.Add(button);
		}
	}

	private void ColorMenu_Click(object sender, RoutedEventArgs e)
	{
		if (_scene.Selected == null)
		{
			Log("Color: select a part first.");
			StatusText.Text = "Select a part first";
			return;
		}
		SyncCustomEditor(_scene.Selected.Color);
		RefreshRecentSwatches();
		_colorBefore = CaptureState("Color");
		_colorLiveChanged = false;
		if (sender is UIElement placementTarget)
		{
			ColorPopup.PlacementTarget = placementTarget;
		}
		base.Dispatcher.BeginInvoke((Func<bool>)(() => ColorPopup.IsOpen = true), DispatcherPriority.Background);
	}

	private void RefreshRecentSwatches()
	{
		RecentGrid.Children.Clear();
		List<string> recentColors = AppSettings.Current.RecentColors;
		bool flag = false;
		foreach (string item in recentColors.Take(10))
		{
			if (PartColor.TryParseRgb(item, out var color) || PartColor.TryParseHex(item, out color))
			{
				System.Windows.Controls.Button button = new System.Windows.Controls.Button
				{
					Width = 26.0,
					Height = 24.0,
					Margin = new Thickness(2.0),
					Background = new SolidColorBrush(color),
					BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102)),
					BorderThickness = new Thickness(1.0),
					ToolTip = item,
					Tag = new PartColor("Recent", color.R, color.G, color.B)
				};
				button.Click += Swatch_Click;
				RecentGrid.Children.Add(button);
				flag = true;
			}
		}
		RecentLabel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		RecentGrid.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
	}

	private static void RecordRecent(Color4 c)
	{
		string item = PartColor.ToRgbText(System.Windows.Media.Color.FromRgb(PartColor.ToByte(c.R), PartColor.ToByte(c.G), PartColor.ToByte(c.B)));
		List<string> recentColors = AppSettings.Current.RecentColors;
		recentColors.Remove(item);
		recentColors.Insert(0, item);
		while (recentColors.Count > 10)
		{
			recentColors.RemoveAt(recentColors.Count - 1);
		}
		AppSettings.Current.Save();
	}

	private void LiveApplyCustom()
	{
		if (_scene.Selected == null || !(NewPreview.Background is SolidColorBrush solidColorBrush))
		{
			return;
		}
		Color4 color = PartColor.FromMediaColor(solidColorBrush.Color);
		foreach (SceneObject item in DragTargets())
		{
			item.Color = color;
		}
		_colorLiveChanged = true;
		_needsFrame = true;
		UpdateLivePropsThrottled();
	}

	private void ColorPopup_Closed(object? sender, EventArgs e)
	{
		if (_colorLiveChanged && _colorBefore != null)
		{
			PushCaptured(_colorBefore, mergeable: false);
			if (NewPreview.Background is SolidColorBrush solidColorBrush)
			{
				RecordRecent(PartColor.FromMediaColor(solidColorBrush.Color));
			}
		}
		_colorBefore = null;
		_colorLiveChanged = false;
	}

	private void Swatch_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: PartColor tag })
		{
			ApplyPartColor(tag.Color, tag.Name);
			ColorPopup.IsOpen = false;
		}
	}

	private (double R, double G, double B) CustomRgb01()
	{
		System.Windows.Media.Color color = PartColor.HsvToRgb(_customH, _customS, _customV);
		return (R: (double)(int)color.R / 255.0, G: (double)(int)color.G / 255.0, B: (double)(int)color.B / 255.0);
	}

	private void SetCustomFromRgb01(double r, double g, double b)
	{
		PartColor.RgbToHsv(System.Windows.Media.Color.FromRgb((byte)Math.Round(Math.Clamp(r, 0.0, 1.0) * 255.0), (byte)Math.Round(Math.Clamp(g, 0.0, 1.0) * 255.0), (byte)Math.Round(Math.Clamp(b, 0.0, 1.0) * 255.0)), out _customH, out _customS, out _customV);
	}

	private double FixedComp()
	{
		var (num, num2, num3) = CustomRgb01();
		return _colorMode switch
		{
			ColorMode.H => _customH, 
			ColorMode.S => _customS, 
			ColorMode.V => _customV, 
			ColorMode.R => num, 
			ColorMode.G => num2, 
			_ => num3, 
		};
	}

	private void BuildColorMaps()
	{
		_mapsDirty = true;
		RenderSVSquare();
		RenderHueStrip();
		HighlightModeButtons();
	}

	private void ColorMode_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: string tag } && Enum.TryParse<ColorMode>(tag, out var result) && result != _colorMode)
		{
			_colorMode = result;
			_mapsDirty = true;
			RefreshCustomPreview();
			HighlightModeButtons();
		}
	}

	private void HighlightModeButtons()
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126, 34));
		SolidColorBrush solidColorBrush2 = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
		foreach (object child in ModeButtons.Children)
		{
			if (child is System.Windows.Controls.Button button)
			{
				button.BorderBrush = ((button.Tag as string == _colorMode.ToString()) ? solidColorBrush : solidColorBrush2);
			}
		}
	}

	private void RenderSVSquare()
	{
		WriteableBitmap writeableBitmap = new WriteableBitmap(200, 200, 96.0, 96.0, PixelFormats.Bgr32, null);
		byte[] array = new byte[160000];
		(double R, double G, double B) tuple = CustomRgb01();
		double item = tuple.R;
		double item2 = tuple.G;
		double item3 = tuple.B;
		byte b = (byte)Math.Round(item * 255.0);
		byte b2 = (byte)Math.Round(item2 * 255.0);
		byte b3 = (byte)Math.Round(item3 * 255.0);
		for (int i = 0; i < 200; i++)
		{
			for (int j = 0; j < 200; j++)
			{
				double num = (double)j / 199.0;
				double num2 = 1.0 - (double)i / 199.0;
				byte b4;
				byte b5;
				byte b6;
				switch (_colorMode)
				{
				case ColorMode.S:
				{
					System.Windows.Media.Color color3 = PartColor.HsvToRgb(num * 360.0, _customS, num2);
					b4 = color3.R;
					b5 = color3.G;
					b6 = color3.B;
					break;
				}
				case ColorMode.V:
				{
					System.Windows.Media.Color color2 = PartColor.HsvToRgb(num * 360.0, num2, _customV);
					b4 = color2.R;
					b5 = color2.G;
					b6 = color2.B;
					break;
				}
				case ColorMode.R:
					b4 = b;
					b5 = (byte)Math.Round(num * 255.0);
					b6 = (byte)Math.Round(num2 * 255.0);
					break;
				case ColorMode.G:
					b4 = (byte)Math.Round(num * 255.0);
					b5 = b2;
					b6 = (byte)Math.Round(num2 * 255.0);
					break;
				case ColorMode.B:
					b4 = (byte)Math.Round(num * 255.0);
					b5 = (byte)Math.Round(num2 * 255.0);
					b6 = b3;
					break;
				default:
				{
					System.Windows.Media.Color color = PartColor.HsvToRgb(_customH, num, num2);
					b4 = color.R;
					b5 = color.G;
					b6 = color.B;
					break;
				}
				}
				int num3 = (i * 200 + j) * 4;
				array[num3] = b6;
				array[num3 + 1] = b5;
				array[num3 + 2] = b4;
				array[num3 + 3] = byte.MaxValue;
			}
		}
		writeableBitmap.WritePixels(new Int32Rect(0, 0, 200, 200), array, 800, 0);
		SVImage.Source = writeableBitmap;
	}

	private void RenderHueStrip()
	{
		WriteableBitmap writeableBitmap = new WriteableBitmap(22, 200, 96.0, 96.0, PixelFormats.Bgr32, null);
		byte[] array = new byte[17600];
		(double R, double G, double B) tuple = CustomRgb01();
		double item = tuple.R;
		double item2 = tuple.G;
		double item3 = tuple.B;
		byte b = (byte)Math.Round(item * 255.0);
		byte b2 = (byte)Math.Round(item2 * 255.0);
		byte b3 = (byte)Math.Round(item3 * 255.0);
		for (int i = 0; i < 200; i++)
		{
			for (int j = 0; j < 22; j++)
			{
				double num = (double)i / 199.0;
				byte b4;
				byte b5;
				byte b6;
				switch (_colorMode)
				{
				case ColorMode.S:
				{
					System.Windows.Media.Color color3 = PartColor.HsvToRgb(_customH, 1.0 - num, _customV);
					b4 = color3.R;
					b5 = color3.G;
					b6 = color3.B;
					break;
				}
				case ColorMode.V:
				{
					System.Windows.Media.Color color2 = PartColor.HsvToRgb(_customH, _customS, 1.0 - num);
					b4 = color2.R;
					b5 = color2.G;
					b6 = color2.B;
					break;
				}
				case ColorMode.R:
					b4 = (byte)Math.Round((1.0 - num) * 255.0);
					b5 = b2;
					b6 = b3;
					break;
				case ColorMode.G:
					b4 = b;
					b5 = (byte)Math.Round((1.0 - num) * 255.0);
					b6 = b3;
					break;
				case ColorMode.B:
					b4 = b;
					b5 = b2;
					b6 = (byte)Math.Round((1.0 - num) * 255.0);
					break;
				default:
				{
					System.Windows.Media.Color color = PartColor.HsvToRgb(num * 360.0, 1.0, 1.0);
					b4 = color.R;
					b5 = color.G;
					b6 = color.B;
					break;
				}
				}
				int num2 = (i * 22 + j) * 4;
				array[num2] = b6;
				array[num2 + 1] = b5;
				array[num2 + 2] = b4;
				array[num2 + 3] = byte.MaxValue;
			}
		}
		writeableBitmap.WritePixels(new Int32Rect(0, 0, 22, 200), array, 88, 0);
		HueImage.Source = writeableBitmap;
	}

	private void SV_Down(object sender, MouseButtonEventArgs e)
	{
		SVCanvas.CaptureMouse();
		SVPick(e.GetPosition(SVCanvas));
	}

	private void SV_Move(object sender, MouseEventArgs e)
	{
		if (SVCanvas.IsMouseCaptured)
		{
			SVPick(e.GetPosition(SVCanvas));
		}
	}

	private void SV_Up(object sender, MouseButtonEventArgs e)
	{
		if (SVCanvas.IsMouseCaptured)
		{
			SVCanvas.ReleaseMouseCapture();
		}
	}

	private void SVPick(System.Windows.Point p)
	{
		double num = Math.Clamp(p.X / 199.0, 0.0, 1.0);
		double num2 = Math.Clamp(1.0 - p.Y / 199.0, 0.0, 1.0);
		var (r, g, b) = CustomRgb01();
		switch (_colorMode)
		{
		case ColorMode.S:
			_customH = num * 360.0;
			_customV = num2;
			break;
		case ColorMode.V:
			_customH = num * 360.0;
			_customS = num2;
			break;
		case ColorMode.R:
			SetCustomFromRgb01(r, num, num2);
			break;
		case ColorMode.G:
			SetCustomFromRgb01(num, g, num2);
			break;
		case ColorMode.B:
			SetCustomFromRgb01(num, num2, b);
			break;
		default:
			_customS = num;
			_customV = num2;
			break;
		}
		RefreshCustomPreview();
		LiveApplyCustom();
	}

	private void Hue_Down(object sender, MouseButtonEventArgs e)
	{
		HueCanvas.CaptureMouse();
		HuePick(e.GetPosition(HueCanvas));
	}

	private void Hue_Move(object sender, MouseEventArgs e)
	{
		if (HueCanvas.IsMouseCaptured)
		{
			HuePick(e.GetPosition(HueCanvas));
		}
	}

	private void Hue_Up(object sender, MouseButtonEventArgs e)
	{
		if (HueCanvas.IsMouseCaptured)
		{
			HueCanvas.ReleaseMouseCapture();
		}
	}

	private void HuePick(System.Windows.Point p)
	{
		double num = Math.Clamp(p.Y / 199.0, 0.0, 1.0);
		var (r, g, b) = CustomRgb01();
		switch (_colorMode)
		{
		case ColorMode.S:
			_customS = 1.0 - num;
			break;
		case ColorMode.V:
			_customV = 1.0 - num;
			break;
		case ColorMode.R:
			SetCustomFromRgb01(1.0 - num, g, b);
			break;
		case ColorMode.G:
			SetCustomFromRgb01(r, 1.0 - num, b);
			break;
		case ColorMode.B:
			SetCustomFromRgb01(r, g, 1.0 - num);
			break;
		default:
			_customH = num * 360.0;
			break;
		}
		RefreshCustomPreview();
		LiveApplyCustom();
	}

	private void RefreshCustomPreview()
	{
		System.Windows.Media.Color color = PartColor.HsvToRgb(_customH, _customS, _customV);
		NewPreview.Background = new SolidColorBrush(color);
		RgbBox.Text = $"{color.R:X2}{color.G:X2}{color.B:X2}";
		(double R, double G, double B) tuple = CustomRgb01();
		double item = tuple.R;
		double item2 = tuple.G;
		double item3 = tuple.B;
		double num = ((_colorMode == ColorMode.H) ? _customH : FixedComp());
		double num2 = ((_rMode != _colorMode) ? double.NaN : ((_colorMode == ColorMode.H) ? _rH : ((_colorMode == ColorMode.S) ? _rS : ((_colorMode == ColorMode.V) ? _rV : ((_colorMode == ColorMode.R) ? _rR : ((_colorMode == ColorMode.G) ? _rG : _rB))))));
		if (_mapsDirty || _rMode != _colorMode || Math.Abs(num - num2) > 0.002)
		{
			RenderSVSquare();
		}
		if (_mapsDirty || _rMode != _colorMode || (Math.Abs(_customH - _rH) > 0.002 && _colorMode != ColorMode.H) || (Math.Abs(_customS - _rS) > 0.002 && _colorMode != ColorMode.S) || (Math.Abs(_customV - _rV) > 0.002 && _colorMode != ColorMode.V) || (Math.Abs(item - _rR) > 0.002 && _colorMode != ColorMode.R) || (Math.Abs(item2 - _rG) > 0.002 && _colorMode != ColorMode.G) || (Math.Abs(item3 - _rB) > 0.002 && _colorMode != ColorMode.B))
		{
			RenderHueStrip();
		}
		_mapsDirty = false;
		_rMode = _colorMode;
		_rH = _customH;
		_rS = _customS;
		_rV = _customV;
		_rR = item;
		_rG = item2;
		_rB = item3;
		double num3 = _colorMode switch
		{
			ColorMode.S => _customH / 360.0, 
			ColorMode.V => _customH / 360.0, 
			ColorMode.R => item2, 
			ColorMode.G => item, 
			ColorMode.B => item, 
			_ => _customS, 
		};
		double num4 = _colorMode switch
		{
			ColorMode.S => 1.0 - _customV, 
			ColorMode.V => 1.0 - _customS, 
			ColorMode.R => 1.0 - item3, 
			ColorMode.G => 1.0 - item3, 
			ColorMode.B => 1.0 - item2, 
			_ => 1.0 - _customV, 
		};
		double num5 = num3 * 199.0;
		double num6 = num4 * 199.0;
		Canvas.SetLeft(SVMarker, num5 - 6.0);
		Canvas.SetTop(SVMarker, num6 - 6.0);
        System.Windows.Shapes.Line sVLineH = SVLineH;
		double y = (SVLineH.Y2 = num6);
		sVLineH.Y1 = y;
        System.Windows.Shapes.Line sVLineV = SVLineV;
		y = (SVLineV.X2 = num5);
		sVLineV.X1 = y;
		double value = ((_colorMode == ColorMode.H) ? (_customH / 360.0) : (1.0 - FixedComp()));
		Canvas.SetTop(HueMarker, Math.Clamp(value, 0.0, 1.0) * 199.0 - 2.0);
	}

	private void SyncCustomEditor(Color4 c)
	{
		System.Windows.Media.Color color = System.Windows.Media.Color.FromRgb(PartColor.ToByte(c.R), PartColor.ToByte(c.G), PartColor.ToByte(c.B));
		CurrentPreview.Background = new SolidColorBrush(color);
		PartColor.RgbToHsv(color, out _customH, out _customS, out _customV);
		_mapsDirty = true;
		RefreshCustomPreview();
	}

	private void RgbBox_Commit(object sender, RoutedEventArgs e)
	{
		ApplyRgb();
	}

	private void RgbBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			ApplyRgb();
			e.Handled = true;
		}
	}

	private void ApplyRgb()
	{
		if (!PartColor.TryParseRgb(RgbBox.Text, out var color) && !PartColor.TryParseHex(RgbBox.Text, out color))
		{
			RgbBox.Text = PreviewRgb();
			return;
		}
		PartColor.RgbToHsv(color, out _customH, out _customS, out _customV);
		RefreshCustomPreview();
		LiveApplyCustom();
	}

	private string PreviewRgb()
	{
		System.Windows.Media.Color color = ((SolidColorBrush)NewPreview.Background).Color;
		return $"{color.R:X2}{color.G:X2}{color.B:X2}";
	}

	private void ApplyCustom_Click(object sender, RoutedEventArgs e)
	{
		System.Windows.Media.Color color = ((SolidColorBrush)NewPreview.Background).Color;
		ApplyPartColor(PartColor.FromMediaColor(color), PreviewRgb());
		ColorPopup.IsOpen = false;
	}

	private void ApplyPartColor(Color4 c, string label)
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			return;
		}
		PushUndo("Color", mergeable: false);
		RecordRecent(c);
		foreach (SceneObject item in DragTargets())
		{
			item.Color = c;
		}
		UpdateLiveProps();
		MarkDirty();
		_needsFrame = true;
		Log($"Color ? {label} on {selected.Name}.");
		StatusText.Text = selected.Name + ": " + label;
	}

	private void BuildMaterials()
	{
		MaterialKind[] values = Enum.GetValues<MaterialKind>();
		for (int i = 0; i < values.Length; i++)
		{
			MaterialKind materialKind = values[i];
			MaterialParams materialParams = MaterialParams.Of(materialKind);
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0),
				Padding = new Thickness(8.0, 5.0, 8.0, 5.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70)),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85)),
				BorderThickness = new Thickness(1.0),
				Foreground = System.Windows.Media.Brushes.White,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Tag = materialKind,
				Content = new StackPanel
				{
					Children = 
					{
						(UIElement)new TextBlock
						{
							Text = materialKind.ToString(),
							FontWeight = FontWeights.SemiBold,
							Foreground = System.Windows.Media.Brushes.White
						},
						(UIElement)new TextBlock
						{
							Text = materialParams.Blurb,
							FontSize = 11.0,
							Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170))
						}
					}
				}
			};
			button.Click += Material_Click;
			MaterialList.Children.Add(button);
		}
	}

	private void MaterialMenu_Click(object sender, RoutedEventArgs e)
	{
		if (_scene.Selected == null)
		{
			Log("Material: select a part first.");
			StatusText.Text = "Select a part first";
			return;
		}
		if (sender is UIElement placementTarget)
		{
			MaterialPopup.PlacementTarget = placementTarget;
		}
		MaterialPopup.IsOpen = true;
	}

	private void ReloadTextures_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string msg = _scene.ReloadMaterialTextures();
			Log(msg);
			StatusText.Text = "Material textures reloaded";
			_needsFrame = true;
		}
		catch (Exception ex)
		{
			Log("Textures reload failed: " + ex.Message);
			StatusText.Text = "Textures reload failed";
		}
	}

	private void Material_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: var tag } && tag is MaterialKind kind)
		{
			ApplyMaterial(kind);
			MaterialPopup.IsOpen = false;
		}
	}

	private void ApplyMaterial(MaterialKind kind)
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			return;
		}
		PushUndo("Material", mergeable: false);
		foreach (SceneObject item in DragTargets())
		{
			item.Material = kind;
		}
		_liveRows = ObjectProperties(selected);
		SetPropertyRows(_liveRows);
		MarkDirty();
		_needsFrame = true;
		Log($"Material ? {kind} on {selected.Name}.");
		StatusText.Text = $"{selected.Name}: {kind}";
	}

	private void ApplyDecal_Click(object sender, RoutedEventArgs e)
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			StatusText.Text = "Select a part first";
			Log("Decal: select a part first.");
		}
		else
		{
			ApplyDecalDialog(selected, "Front");
		}
	}

	private bool ApplyDecalDialog(SceneObject target, string face)
	{
		target.MigrateSingleToList();
		if (target.Decals.Count >= 6)
		{
			StatusText.Text = $"Max {6} decals per part";
			Log($"Decal: {target.Name} already has {target.Decals.Count} (max). Remove one first.");
			return false;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = $"Import decal image ({target.Decals.Count + 1} of {6})",
			Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			return false;
		}
		ApplyDecalPath(target, openFileDialog.FileName, face);
		return true;
	}

	private void ApplyDecalPath(SceneObject target, string path, string face)
	{
		PushUndo("Decal", mergeable: false);
		target.Decals.Add(new DecalLayer
		{
			Image = path,
			Face = face
		});
		_liveRows = ObjectProperties(target);
		SetPropertyRows(_liveRows);
		_needsFrame = true;
		Log($"Decal {target.Decals.Count} applied to {target.Name} ({face}): {path}");
		StatusText.Text = "Decal applied";
		MarkDirty();
	}

	private void ApplyText_Click(object sender, RoutedEventArgs e)
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			StatusText.Text = "Select a part first";
			Log("Text: select a part first.");
		}
		else if (selected.Texts.Count >= 6)
		{
			StatusText.Text = $"Max {6} texts per part";
			Log($"Text: {selected.Name} already has {selected.Texts.Count} (max). Remove one first.");
		}
		else
		{
			PushUndo("Add text", mergeable: false);
			selected.Texts.Add(new TextLayer
			{
				Text = "Text",
				Face = "Front"
			});
			_liveRows = ObjectProperties(selected);
			SetPropertyRows(_liveRows);
			_needsFrame = true;
			MarkDirty();
			Log($"Text {selected.Texts.Count} added to {selected.Name}.");
			StatusText.Text = "Text added";
		}
	}

	private void ExplorerTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
	{
		if (!_syncingTree && e.NewValue is SceneNode n)
		{
			ShowNode(n);
		}
	}

	private IEnumerable<SceneObject> DragTargets()
	{
		return _selection.Where(_scene.Objects.Contains);
	}

	private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
	{
		while (node != null)
		{
			if (node is T result)
			{
				return result;
			}
			node = VisualTreeHelper.GetParent(node);
		}
		return null;
	}

	private void ExplorerTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_physics.Simulating)
		{
			return;
		}
		object originalSource = e.OriginalSource;
		if ((!(originalSource is TextBlock) && !(originalSource is System.Windows.Controls.Image)) || !(FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext is SceneNode sceneNode))
		{
			return;
		}
		if (sceneNode.Tag is SceneNode.WaypointRef { Obj: not null, Index: >=0 } waypointRef && waypointRef.Index < waypointRef.Obj.Waypoints.Count && _scene.Objects.Contains(waypointRef.Obj))
		{
			_scene.SelWaypointObj = waypointRef.Obj;
			_scene.SelWaypointIndex = waypointRef.Index;
			StatusText.Text = $"Waypoint {waypointRef.Index + 1} of {waypointRef.Obj.Name} selected";
			_needsFrame = true;
			e.Handled = true;
		}
		else if (sceneNode.Tag is SceneObject obj)
		{
			if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			{
				ToggleObjectSelection(obj);
			}
			else
			{
				SelectObject(obj);
			}
			e.Handled = true;
		}
	}

	private void ShowNode(SceneNode n)
	{
		if (_physics.Simulating)
		{
			return;
		}
		if (n.Tag is SceneNode.WaypointRef { Obj: not null, Index: >=0 } waypointRef && waypointRef.Index < waypointRef.Obj.Waypoints.Count && _scene.Objects.Contains(waypointRef.Obj))
		{
			_scene.SelWaypointObj = waypointRef.Obj;
			_scene.SelWaypointIndex = waypointRef.Index;
			StatusText.Text = $"Waypoint {waypointRef.Index + 1} of {waypointRef.Obj.Name} selected";
			_needsFrame = true;
			return;
		}
		if (n.Tag is SceneObject obj)
		{
			SelectObject(obj);
			StatusText.Text = "Selected: " + n.Name;
			return;
		}
		_selection.Clear();
		_scene.Selected = null;
		_scene.SelectedObjects.Clear();
		bool flag = n.Tag == null;
		if (flag)
		{
			bool flag2;
			switch (n.Name)
			{
			case "Sun":
			case "Atmosphere":
			case "Lighting":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			_liveObj = null;
			_liveService = n.Name;
			_liveRows = ServiceProperties(n.Name);
			SetPropertyRows(_liveRows);
			SelectedLabel.Text = n.Name + " [" + n.Type + "]";
			RefreshExplorer();
			SelectTreeNode(n);
		}
		else
		{
			_liveObj = null;
			_liveService = null;
			_liveRows = null;
			SelectedLabel.Text = n.Name + " [" + n.Type + "]";
			SetPropertyRows(DefaultProperties(n.Name, n.Type));
			RefreshExplorer();
			SelectTreeNode(n);
		}
		StatusText.Text = "Selected: " + n.Name;
		SyncAnchorToggle();
		SyncInspectorHeader();
	}

	private void SetPropertyRows(IEnumerable<PropertyRow> rows)
	{
		_propFull = rows.ToList();
		if (PropertiesList != null)
		{
			ApplyPropertyFilter();
		}
	}

	private void PropertiesFilter_Changed(object sender, TextChangedEventArgs e)
	{
		if (!_propFilterSync && PropertiesList != null)
		{
			ApplyPropertyFilter();
		}
	}

	private void ApplyPropertyFilter()
	{
		if (PropertiesList == null)
		{
			return;
		}
		string q = (PropertiesFilter?.Text ?? "").Trim();
		if (q == "Search" || q.StartsWith("??"))
		{
			q = "";
		}
		foreach (var r in _propFull) r.HideEditor = false;
		List<PropertyRow> list;
		if (string.IsNullOrWhiteSpace(q))
		{
			// No filter: collapsed sections keep their header row (so the
			// chevron bar survives to re-expand) and hide only body rows.
			list = new List<PropertyRow>(_propFull.Count);
			var headerKept = new HashSet<string>(StringComparer.Ordinal);
			foreach (var r in _propFull)
			{
				if (!_collapsedCats.Contains(r.Category)) list.Add(r);
				else if (headerKept.Add(r.Category)) list.Add(r);
			}
		}
		else
		{
			list = _propFull.Where((PropertyRow r) => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		string text = null;
		foreach (PropertyRow item in list)
		{
			item.ShowCategory = item.Category != text;
			text = item.Category;
			bool collapsed = _collapsedCats.Contains(item.Category);
			item.CatChevron = (collapsed ? "\u25B8" : "\u25BE");
			item.HideEditor = collapsed && item.ShowCategory && string.IsNullOrWhiteSpace(q);
		}
		_propFilterSync = true;
		PropertiesList.ItemsSource = list;
		if (PropHint != null)
		{
			PropHint.Text = (string.IsNullOrWhiteSpace(q) ? "Enter commits \u00B7 Esc reverts" : $"{list.Count} of {_propFull.Count} match \"{q}\"");
		}
		_propFilterSync = false;
	}

	private void CategoryToggle_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: PropertyRow dataContext })
		{
			if (!_collapsedCats.Remove(dataContext.Category))
			{
				_collapsedCats.Add(dataContext.Category);
			}
			ApplyPropertyFilter();
		}
	}

	private void SyncInspectorHeader()
	{
		if (InspectorIcon == null || InspectorName == null)
		{
			return;
		}
		SceneObject selected = _scene.Selected;
		if (selected != null)
		{
			InspectorIcon.Source = IconSource(PartIcon(selected).IconPath);
			if (!InspectorName.IsKeyboardFocusWithin)
			{
				InspectorName.Text = selected.Name;
			}
			InspectorName.IsEnabled = true;
		}
		else if (_liveService != null)
		{
			InspectorIcon.Source = IconSource((_liveService == "Sun") ? "Icons/sun.png" : ((_liveService == "Atmosphere") ? "Icons/atmosphere.png" : "Icons/light.png"));
			if (!InspectorName.IsKeyboardFocusWithin)
			{
				InspectorName.Text = _liveService;
			}
			InspectorName.IsEnabled = false;
		}
		else
		{
			InspectorIcon.Source = IconSource("Icons/object.png");
			if (!InspectorName.IsKeyboardFocusWithin)
			{
				InspectorName.Text = "Nothing selected";
			}
			InspectorName.IsEnabled = false;
		}
	}

	private static ImageSource IconSource(string path)
	{
		try
		{
			return new BitmapImage(new Uri(path, UriKind.Relative));
		}
		catch
		{
			return new BitmapImage();
		}
	}

	private void InspectorName_GotFocus(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.TextBox textBox)
		{
			textBox.SelectAll();
		}
	}

	private void InspectorName_Commit(object sender, RoutedEventArgs e)
	{
		CommitInspectorName();
	}

	private void InspectorName_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			CommitInspectorName();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			InspectorName.Text = _scene.Selected?.Name ?? "";
			ViewportControl.Focus();
			e.Handled = true;
		}
	}

	private void CommitInspectorName()
	{
		if (_scene.Selected != null && _liveObj != null)
		{
			CommitProperty(new PropertyRow
			{
				Category = "General",
				Name = "Name",
				Value = InspectorName.Text
			});
			SyncInspectorHeader();
		}
	}

	private void AddComponent_Click(object sender, RoutedEventArgs e)
	{
		if (_liveObj == null || _scene.Selected == null)
		{
			StatusText.Text = "Select a part first";
		}
		else
		{
			AddComponentButton.ContextMenu.IsOpen = true;
		}
	}

	private void AddComponentItem_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.MenuItem { Tag: string tag })
		{
			PropertyAction_Click(new System.Windows.Controls.MenuItem
			{
				DataContext = new PropertyRow
				{
					Category = "Add",
					Name = tag
				}
			}, new RoutedEventArgs());
		}
	}

	private void UpdateLiveProps()
	{
		UpdateSelText();
		SceneObject liveObj = _liveObj;
		if (liveObj != null && _liveRows != null)
		{
			if (liveObj.Shape == ShapeKind.None && liveObj.Light != LightKind.None)
			{
				foreach (PropertyRow liveRow in _liveRows)
				{
					UpdateLightRow(liveRow, liveObj);
				}
			}
			else
			{
				foreach (PropertyRow liveRow2 in _liveRows)
				{
					switch (liveRow2.Name)
					{
					case "Name":
						liveRow2.Value = liveObj.Name;
						break;
					case "Position":
						liveRow2.Value = PosValue(liveObj);
						break;
					case "Size":
						if (liveRow2.TextIndex >= 0)
						{
							if (liveRow2.TextIndex < liveObj.Texts.Count)
							{
								liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Size.ToString("0", CultureInfo.InvariantCulture);
							}
						}
						else
						{
							liveRow2.Value = SizeValue(liveObj);
						}
						break;
					case "Rotation":
						liveRow2.Value = RotValue(liveObj);
						break;
					case "Shape":
						liveRow2.Value = liveObj.Shape.ToString();
						break;
					case "Color":
						if (liveRow2.TextIndex >= 0)
						{
							if (liveRow2.TextIndex < liveObj.Texts.Count)
							{
								liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Color;
								liveRow2.Swatch = TextSwatch(liveObj.Texts[liveRow2.TextIndex].Color);
							}
						}
						else
						{
							liveRow2.Value = ColorValue(liveObj);
							liveRow2.Swatch = ColorBrush(liveObj.Color);
						}
						break;
					case "Material":
						liveRow2.Value = liveObj.Material.ToString();
						break;
					case "Transparency":
						liveRow2.Value = liveObj.Transparency.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Reflectance":
					case "Reflection":
						liveRow2.Value = liveObj.Reflectance.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "CastShadow":
						liveRow2.Value = liveObj.CastShadow.ToString();
						break;
					case "Anchored":
						liveRow2.Value = liveObj.Anchored.ToString();
						break;
					case "Locked":
						liveRow2.Value = liveObj.Locked.ToString();
						break;
					case "Spawn":
						liveRow2.Value = liveObj.IsSpawn.ToString();
						break;
					case "Effect":
						liveRow2.Value = liveObj.ParticlePreset;
						break;
					case "Mass":
						liveRow2.Value = liveObj.Mass.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Damage":
						liveRow2.Value = liveObj.Damage.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Tiling":
						liveRow2.Value = liveObj.Tiling.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Color Brightness":
						liveRow2.Value = liveObj.ColorBrightness.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Rate":
						liveRow2.Value = liveObj.EmissionRate.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Lifetime":
						liveRow2.Value = liveObj.ParticleLifetime.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Particle speed":
						liveRow2.Value = liveObj.ParticleSpeed.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Particle size":
						liveRow2.Value = liveObj.ParticleSize.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Spread":
						liveRow2.Value = liveObj.ParticleSpread.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Gravity":
						liveRow2.Value = liveObj.ParticleGravity.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "TeleportLink":
						liveRow2.Value = liveObj.TeleportLink;
						break;
					case "CanCollide":
						liveRow2.Value = liveObj.CanCollide.ToString();
						break;
					case "Mesh file":
						liveRow2.Value = liveObj.MeshPath ?? "";
						break;
					case "Collision fidelity":
						liveRow2.Value = liveObj.CollisionFidelity.ToString();
						break;
					case "Render fidelity":
						liveRow2.Value = liveObj.RenderFidelity.ToString();
						break;
					case "Sound":
						liveRow2.Value = liveObj.SoundPath ?? "";
						break;
					case "Volume":
						liveRow2.Value = liveObj.SoundVolume.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Looped":
						liveRow2.Value = liveObj.SoundLooped.ToString();
						break;
					case "Playing":
						liveRow2.Value = liveObj.SoundPlaying.ToString();
						break;
					case "Play on touch":
						liveRow2.Value = liveObj.SoundPlayOnTouch.ToString();
						break;
					case "Hidden time":
						liveRow2.Value = liveObj.TimedHidden.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Timed offset":
						liveRow2.Value = liveObj.TimedOffset.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Visible time":
						liveRow2.Value = liveObj.TimedVisible.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Bounce power":
						liveRow2.Value = liveObj.BouncePower.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Magnet power":
						liveRow2.Value = liveObj.MagnetPower.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Magnet range":
						liveRow2.Value = liveObj.MagnetRange.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Conveyor direction":
						liveRow2.Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", liveObj.ConveyorDirection.X, liveObj.ConveyorDirection.Y, liveObj.ConveyorDirection.Z);
						break;
					case "Conveyor speed":
						liveRow2.Value = liveObj.ConveyorSpeed.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Move distance":
						liveRow2.Value = liveObj.MoveDistance.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Move speed":
						liveRow2.Value = liveObj.MoveSpeed.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Spin speed":
						liveRow2.Value = liveObj.SpinSpeed.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Hits to break":
						liveRow2.Value = liveObj.BreakHits.ToString(CultureInfo.InvariantCulture);
						break;
					case "Direction":
						liveRow2.Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", liveObj.VelocityDirection.X, liveObj.VelocityDirection.Y, liveObj.VelocityDirection.Z);
						break;
					case "Speed":
						liveRow2.Value = liveObj.VelocitySpeed.ToString("0.00", CultureInfo.InvariantCulture);
						break;
					case "Speeding Mode":
						liveRow2.Value = ((liveObj.VelocityMode == SpeedMode.Accelerating) ? "Accelerating" : "Always/Stable");
						break;
					case "NPC Type":
						liveRow2.Value = liveObj.NpcType.ToString();
						break;
					case "NPC Damage":
						liveRow2.Value = liveObj.NpcDamage.ToString("0.0", CultureInfo.InvariantCulture);
						break;
					case "NPC Speed":
						liveRow2.Value = liveObj.NpcSpeed.ToString("0.0", CultureInfo.InvariantCulture);
						break;
					case "Dialogue":
						liveRow2.Value = liveObj.NpcDialogue;
						break;
					case "Chat Range":
						liveRow2.Value = liveObj.NpcChatRange.ToString("0.0", CultureInfo.InvariantCulture);
						break;
					case "Chat Pause":
						liveRow2.Value = liveObj.NpcChatInterval.ToString("0.0", CultureInfo.InvariantCulture);
						break;
					case "Image":
						if (liveRow2.DecalIndex >= 0)
						{
							if (liveRow2.DecalIndex < liveObj.Decals.Count)
							{
								SyncThumbnail(liveRow2, liveObj.Decals[liveRow2.DecalIndex].Image);
							}
						}
						else
						{
							SyncThumbnail(liveRow2, liveObj.DecalImage ?? "None");
						}
						break;
					case "Texture":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							SyncThumbnail(liveRow2, liveObj.Textures[liveRow2.TextureIndex].Image);
						}
						break;
					case "Texture Face":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							liveRow2.Value = liveObj.Textures[liveRow2.TextureIndex].Face;
						}
						break;
					case "Texture Transparency":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							liveRow2.Value = liveObj.Textures[liveRow2.TextureIndex].Transparency.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Tiling X":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							liveRow2.Value = liveObj.Textures[liveRow2.TextureIndex].TilingX.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Tiling Y":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							liveRow2.Value = liveObj.Textures[liveRow2.TextureIndex].TilingY.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Tiling Offset X":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							liveRow2.Value = liveObj.Textures[liveRow2.TextureIndex].OffsetX.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Tiling Offset Y":
						if (liveRow2.TextureIndex >= 0 && liveRow2.TextureIndex < liveObj.Textures.Count)
						{
							liveRow2.Value = liveObj.Textures[liveRow2.TextureIndex].OffsetY.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Face":
						if (liveRow2.Type == "file")
						{
							SyncThumbnail(liveRow2, liveObj.NpcFaceImage ?? "None");
						}
						else if (liveRow2.DecalIndex >= 0)
						{
							if (liveRow2.DecalIndex < liveObj.Decals.Count)
							{
								liveRow2.Value = liveObj.Decals[liveRow2.DecalIndex].Face;
							}
						}
						else if (liveRow2.TextIndex >= 0)
						{
							if (liveRow2.TextIndex < liveObj.Texts.Count)
							{
								liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Face;
							}
						}
						else
						{
							liveRow2.Value = liveObj.DecalFace;
						}
						break;
					case "Image Transparency":
						if (liveRow2.DecalIndex >= 0)
						{
							if (liveRow2.DecalIndex < liveObj.Decals.Count)
							{
								liveRow2.Value = liveObj.Decals[liveRow2.DecalIndex].Transparency.ToString("0.00", CultureInfo.InvariantCulture);
							}
						}
						else if (liveRow2.TextIndex >= 0)
						{
							if (liveRow2.TextIndex < liveObj.Texts.Count)
							{
								liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Transparency.ToString("0.00", CultureInfo.InvariantCulture);
							}
						}
						else
						{
							liveRow2.Value = liveObj.DecalTransparency.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Image Blur":
						if (liveRow2.DecalIndex >= 0)
						{
							if (liveRow2.DecalIndex < liveObj.Decals.Count)
							{
								liveRow2.Value = liveObj.Decals[liveRow2.DecalIndex].Blur.ToString("0.00", CultureInfo.InvariantCulture);
							}
						}
						else if (liveRow2.TextIndex >= 0)
						{
							if (liveRow2.TextIndex < liveObj.Texts.Count)
							{
								liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Blur.ToString("0.00", CultureInfo.InvariantCulture);
							}
						}
						else
						{
							liveRow2.Value = liveObj.DecalBlur.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Text":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Text;
						}
						break;
					case "Font":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Font;
						}
						break;
					case "Text Transparency":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Transparency.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Text Blur":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Blur.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					case "Bold":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Bold.ToString();
						}
						break;
					case "Outline":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Outline.ToString("0.0", CultureInfo.InvariantCulture);
						}
						break;
					case "Outline Color":
						if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].OutlineColor;
							liveRow2.Swatch = TextSwatch(liveObj.Texts[liveRow2.TextIndex].OutlineColor);
						}
						break;
					case "Offset":
						if (liveRow2.DecalIndex >= 0)
						{
							if (liveRow2.DecalIndex < liveObj.Decals.Count)
							{
								liveRow2.Value = OffsetValue(liveObj.Decals[liveRow2.DecalIndex].OffsetX, liveObj.Decals[liveRow2.DecalIndex].OffsetY);
							}
						}
						else if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = OffsetValue(liveObj.Texts[liveRow2.TextIndex].OffsetX, liveObj.Texts[liveRow2.TextIndex].OffsetY);
						}
						break;
					case "Scale":
						if (liveRow2.DecalIndex >= 0)
						{
							if (liveRow2.DecalIndex < liveObj.Decals.Count)
							{
								liveRow2.Value = liveObj.Decals[liveRow2.DecalIndex].Scale.ToString("0.00", CultureInfo.InvariantCulture);
							}
						}
						else if (liveRow2.TextIndex >= 0 && liveRow2.TextIndex < liveObj.Texts.Count)
						{
							liveRow2.Value = liveObj.Texts[liveRow2.TextIndex].Scale.ToString("0.00", CultureInfo.InvariantCulture);
						}
						break;
					}
				}
			}
		}
		if (_liveObj == null && _liveService != null && _liveRows != null)
		{
			foreach (PropertyRow liveRow3 in _liveRows)
			{
				UpdateServiceRow(liveRow3);
			}
		}
		if (_scene.Selected != null)
		{
			SelectedLabel.Text = SelectionTitle();
		}
		MarkVaryingRows();
	}

	private static System.Windows.Media.Brush ColorBrush(Color4 c)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(ToByte(c.R), ToByte(c.G), ToByte(c.B)));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static ImageSource? LoadThumbnail(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		lock (_thumbCache)
		{
			if (_thumbCache.TryGetValue(path, out ImageSource value))
			{
				return value;
			}
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
			bitmapImage.DecodePixelWidth = 48;
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			lock (_thumbCache)
			{
				if (_thumbCache.Count > 256)
				{
					_thumbCache.Clear();
				}
				_thumbCache[path] = bitmapImage;
			}
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private static void SyncThumbnail(PropertyRow r, string path)
	{
		if (r.Thumbnail == null || !(r.Value == path))
		{
			r.Value = path;
			r.Thumbnail = LoadThumbnail(path);
		}
	}

	private void RefreshExplorer()
	{
		Dictionary<SceneObject, SceneNode> dictionary = new Dictionary<SceneObject, SceneNode>();
		foreach (SceneNode child in _workspace.Children)
		{
			if (child.Tag is SceneObject key)
			{
				dictionary[key] = child;
			}
		}
		_workspace.Children.Clear();
		HashSet<SceneObject> hashSet = new HashSet<SceneObject>();
		int num = 0;
		foreach (SceneObject @object in _scene.Objects)
		{
			if (@object.IsAvatarFace)
			{
				continue;
			}
			if (!hashSet.Add(@object))
			{
				num++;
				continue;
			}
		if (!dictionary.TryGetValue(@object, out var value))
		{
			value = MakeExplorerNode(@object);
		}
			value.Name = @object.Name;
			value.IsMultiSelected = _selection.Contains(@object);
			SyncWaypointChildren(value, @object);
			_workspace.Children.Add(value);
		}
		if (num > 0)
		{
			Log($"Explorer: scene list repeated {num} object(s) \u2014 each shown once. (What did you click just before this?)");
		}
		_explorerObjectCount = hashSet.Count;
		PartsText.Text = $"{CountObjects()} objects";
	}

	private static SceneNode MakeExplorerNode(SceneObject @object)
	{
		return ((@object.Shape == ShapeKind.None && @object.Light != LightKind.None) ? new SceneNode(@object.Name, $"{@object.Light}Light", "??", "Icons/light.png")
		{
			Tag = @object
		} : new SceneNode(@object.Name, "Part", @object.IsSpawn ? "??" : ((@object.Shape == ShapeKind.Killbrick || @object.IsKillbrick) ? "??" : (@object.IsTeleportPad ? "??" : (@object.IsTimedPart ? "?" : (@object.IsCheckpoint ? "??" : (@object.IsBouncePad ? "??" : "??"))))), @object.IsSpawn ? "Icons/capsuleSpawn.png" : "Icons/object.png")
		{
			Tag = @object
		});
	}

	/// <summary>Incremental explorer removal for Delete: drop only dead nodes (order kept),
	/// no full rebuild. Keeps _explorerObjectCount in sync so the selection path skips its own rebuild.</summary>
	private void RemoveExplorerNodes(List<SceneObject> dead)
	{
		if (dead.Count == 0)
		{
			return;
		}
		HashSet<SceneObject> gone = new HashSet<SceneObject>(dead);
		// Snapshot + Remove (never indexed RemoveAt): removing a selected node
		// fires SelectedItemChanged synchronously, which can rebuild this very
		// list mid-loop (AfterSelectionChanged sees the shrunk scene). Remove
		// on an absent node safely no-ops, so every interleaving stays correct.
		// The sync guard additionally mutes that whole reentrant chain while
		// the batch drops (selection re-syncs once, in the caller, after).
		List<SceneNode> doomed = new List<SceneNode>();
		foreach (SceneNode child in _workspace.Children)
		{
			if (child.Tag is SceneObject tag && gone.Contains(tag))
			{
				doomed.Add(child);
			}
		}
		_syncingTree = true;
		try
		{
			foreach (SceneNode d in doomed)
			{
				_workspace.Children.Remove(d);
			}
		}
		finally
		{
			_syncingTree = false;
		}
		int removed = 0;
		foreach (SceneObject o in dead)
		{
			if (!o.IsAvatarFace)
			{
				removed++;
			}
		}
		_explorerObjectCount -= removed;
		PartsText.Text = $"{CountObjects()} objects";
	}

	/// <summary>Incremental explorer append for Duplicate: appended parts live at the end of
	/// the scene list, so new nodes go at the end too. No full rebuild.</summary>
	private void AppendExplorerNodes(List<SceneObject> added)
	{
		if (added.Count == 0)
		{
			return;
		}
		int count = 0;
		foreach (SceneObject o in added)
		{
			if (o.IsAvatarFace)
			{
				continue;
			}
			SceneNode node = MakeExplorerNode(o);
			node.IsMultiSelected = _selection.Contains(o);
			SyncWaypointChildren(node, o);
			_workspace.Children.Add(node);
			count++;
		}
		_explorerObjectCount += count;
		PartsText.Text = $"{CountObjects()} objects";
	}

	private static void SyncWaypointChildren(SceneNode node, SceneObject o)
	{
		int num = (o.IsMovingPlatform ? o.Waypoints.Count : 0);
		bool flag = node.Children.Count == num;
		if (flag)
		{
			for (int i = 0; i < num; i++)
			{
				if (!(node.Children[i].Tag is SceneNode.WaypointRef waypointRef) || waypointRef.Obj != o || waypointRef.Index != i || node.Children[i].Name != $"Waypoint {i + 1}")
				{
					flag = false;
					break;
				}
			}
		}
		if (!flag)
		{
			node.Children.Clear();
			for (int j = 0; j < num; j++)
			{
				node.Children.Add(new SceneNode($"Waypoint {j + 1}", "Waypoint", "?")
				{
					Tag = new SceneNode.WaypointRef(o, j)
				});
			}
		}
	}

	private void UpdateExplorerSelection()
	{
		foreach (SceneNode child in _workspace.Children)
		{
			if (child.Tag is SceneObject sceneObject)
			{
				child.IsMultiSelected = _selection.Contains(sceneObject);
				SyncWaypointChildren(child, sceneObject);
			}
		}
	}

	private bool IsSelected(SceneObject obj)
	{
		return _selection.Contains(obj);
	}

	private void SelectObject(SceneObject? obj)
	{
		_selection.Clear();
		if (obj != null)
		{
			_selection.Add(obj);
		}
		_scene.Selected = obj;
		AfterSelectionChanged();
	}

	private void ToggleObjectSelection(SceneObject? obj)
	{
		if (obj == null)
		{
			SelectObject(null);
			return;
		}
		if (!_selection.Remove(obj))
		{
			_selection.Add(obj);
			_scene.Selected = obj;
		}
		else if (_scene.Selected == obj)
		{
			_scene.Selected = _selection.FirstOrDefault();
		}
		AfterSelectionChanged();
	}

	private void AfterSelectionChanged()
	{
		_selection.RemoveWhere((SceneObject o) => !_scene.Objects.Contains(o));
		_scene.SelectedObjects.Clear();
		_scene.SelectedObjects.UnionWith(_selection);
		if (_scene.Selected != null && !_selection.Contains(_scene.Selected))
		{
			_scene.Selected = _selection.FirstOrDefault();
		}
		_needsFrame = true;
		SyncGizmoPivot();
		int num = _scene.Objects.Count((SceneObject o) => !o.IsAvatarFace);
		if (_explorerObjectCount != num)
		{
			RefreshExplorer();
		}
		else
		{
			UpdateExplorerSelection();
		}
		if (_scene.Selected != null)
		{
			SyncTreeSelection();
			ShowObject(_scene.Selected);
		}
		else
		{
			ClearTreeHighlights(ExplorerTree);
			_liveObj = null;
			_liveService = null;
			_liveRows = null;
			SelectedLabel.Text = "Nothing selected";
			SetPropertyRows(new List<PropertyRow>());
			SyncAnchorToggle();
			SyncInspectorHeader();
		}
		UpdateSelText();
	}

	private void ShowObject(SceneObject so)
	{
		_liveObj = so;
		_liveService = null;
		if (so.Shape == ShapeKind.None && so.Light != LightKind.None)
		{
			_liveRows = LightProperties(so);
		}
		else
		{
			_liveRows = ObjectProperties(so);
		}
		SetPropertyRows(_liveRows);
		SelectedLabel.Text = SelectionTitle();
		StatusText.Text = "Selected: " + so.Name;
		SyncAnchorToggle();
		SyncInspectorHeader();
		MarkVaryingRows();
	}

	private void UpdateSelText()
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			SelText.Text = "Nothing selected";
			return;
		}
		string text = string.Format(CultureInfo.InvariantCulture, "P {0:0.0}, {1:0.0}, {2:0.0}", selected.Position.X, selected.Position.Y, selected.Position.Z);
		string text2 = string.Format(CultureInfo.InvariantCulture, "S {0:0.0}, {1:0.0}, {2:0.0}", selected.Size.X, selected.Size.Y, selected.Size.Z);
		SelText.Text = ((_selection.Count > 1) ? $"{_selection.Count} selected \u00B7 {text} \u00B7 {text2}" : (text + " \u00B7 " + text2));
	}

	private string SelectionTitle()
	{
		SceneObject selected = _scene.Selected;
		if (selected == null)
		{
			return "Nothing selected";
		}
		if (_selection.Count > 1)
		{
			return $"{_selection.Count} selected \u00B7 {PartLabel(selected)} active";
		}
		return PartLabel(selected);
	}

	private void SyncGizmoPivot()
	{
		if (_scene.Selected != null)
		{
			List<SceneObject> list = DragTargets().ToList();
			_scene.GizmoPivot = ((list.Count == 0) ? _scene.Selected.Position : (list.Aggregate(OpenTK.Mathematics.Vector3.Zero, (OpenTK.Mathematics.Vector3 sum, SceneObject o) => sum + o.Position) / list.Count));
			_scene.GizmoOrientation = _scene.Selected.Orientation;
		}
	}

	private void SyncTreeSelection()
	{
		_syncingTree = true;
		try
		{
			ClearTreeHighlights(ExplorerTree);
			SceneObject target = _scene.Selected;
			if (target == null)
			{
				return;
			}
			ExplorerTree.UpdateLayout();
			if (SelectNodeRecursive(ExplorerTree, target))
			{
				return;
			}
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (_scene.Selected != target)
				{
					return;
				}
				_syncingTree = true;
				try
				{
					ExplorerTree.UpdateLayout();
					SelectNodeRecursive(ExplorerTree, target);
				}
				finally
				{
					_syncingTree = false;
				}
			}, DispatcherPriority.Loaded);
		}
		finally
		{
			_syncingTree = false;
		}
	}

	private void ClearTreeHighlights(ItemsControl parent)
	{
		foreach (object item in (IEnumerable)parent.Items)
		{
			if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
			{
				if (treeViewItem.IsSelected)
				{
					treeViewItem.IsSelected = false;
				}
				ClearTreeHighlights(treeViewItem);
			}
		}
	}

	private void SelectWaypointNode(SceneObject o, int idx)
	{
		foreach (SceneNode child in _workspace.Children)
		{
			if (child.Tag != o)
			{
				continue;
			}
			{
				foreach (SceneNode child2 in child.Children)
				{
					if (child2.Tag is SceneNode.WaypointRef waypointRef && waypointRef.Obj == o && waypointRef.Index == idx)
					{
						SelectTreeNode(child2);
						break;
					}
				}
				break;
			}
		}
	}

	private void SelectTreeNode(SceneNode target)
	{
		_syncingTree = true;
		try
		{
			ClearTreeHighlights(ExplorerTree);
			ExplorerTree.UpdateLayout();
			SelectNodeByItem(ExplorerTree, target);
		}
		finally
		{
			_syncingTree = false;
		}
	}

	private bool SelectNodeByItem(ItemsControl parent, SceneNode target)
	{
		foreach (object item in (IEnumerable)parent.Items)
		{
			if (item == target)
			{
				if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
				{
					treeViewItem.BringIntoView();
					treeViewItem.IsSelected = true;
					return true;
				}
				return false;
			}
			if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem2)
			{
				treeViewItem2.IsExpanded = true;
				treeViewItem2.UpdateLayout();
				if (SelectNodeByItem(treeViewItem2, target))
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool SelectNodeRecursive(ItemsControl parent, SceneObject obj)
	{
		foreach (object item in (IEnumerable)parent.Items)
		{
			if (item is SceneNode sceneNode && sceneNode.Tag == obj)
			{
				if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
				{
					treeViewItem.BringIntoView();
					treeViewItem.IsSelected = true;
					return true;
				}
				return false;
			}
			if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem2)
			{
				treeViewItem2.IsExpanded = true;
				treeViewItem2.UpdateLayout();
				if (SelectNodeRecursive(treeViewItem2, obj))
				{
					return true;
				}
			}
		}
		return false;
	}

	private void PropertiesGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
	{
		if (e.Row.Item is PropertyRow { Name: "Image" } propertyRow)
		{
			SceneObject liveObj = _liveObj;
			if (liveObj != null)
			{
				OpenFileDialog openFileDialog = new OpenFileDialog
				{
					Title = "Import decal image",
					Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
					CheckFileExists = true
				};
				if (openFileDialog.ShowDialog(this) == true)
				{
					if (propertyRow.DecalIndex >= 0 && propertyRow.DecalIndex < liveObj.Decals.Count)
					{
						liveObj.Decals[propertyRow.DecalIndex].Image = openFileDialog.FileName;
					}
					else
					{
						liveObj.MigrateSingleToList();
						if (liveObj.Decals.Count < 6)
						{
							liveObj.Decals.Add(new DecalLayer
							{
								Image = openFileDialog.FileName,
								Face = "Front"
							});
						}
					}
					_liveRows = ObjectProperties(liveObj);
					SetPropertyRows(_liveRows);
				}
				_needsFrame = true;
				e.Cancel = true;
				return;
			}
		}
		if (e.Row.Item is PropertyRow { Name: "Texture" } propertyRow2)
		{
			SceneObject liveObj2 = _liveObj;
			if (liveObj2 != null)
			{
				OpenFileDialog openFileDialog2 = new OpenFileDialog
				{
					Title = "Import texture image",
					Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
					CheckFileExists = true
				};
				if (openFileDialog2.ShowDialog(this) == true && propertyRow2.TextureIndex >= 0 && propertyRow2.TextureIndex < liveObj2.Textures.Count)
				{
					liveObj2.Textures[propertyRow2.TextureIndex].Image = openFileDialog2.FileName;
					_liveRows = ObjectProperties(liveObj2);
					SetPropertyRows(_liveRows);
				}
				_needsFrame = true;
				e.Cancel = true;
				return;
			}
		}
        if (_liveObj == null || !(e.Row.Item is PropertyRow row) || row.IsReadOnly
            || row.IsBoolean || row.IsChoice || row.IsColor || row.IsNumber
            || row.IsAction || row.IsFile)
        {
            e.Cancel = true;
        }
	}

	private void PropertiesGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
	{
		if (!(e.Row.Item is PropertyRow { IsChoice: not false }))
		{
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			System.Windows.Controls.ComboBox comboBox = FindVisualChild<System.Windows.Controls.ComboBox>(e.EditingElement);
			if (comboBox != null)
			{
				comboBox.IsDropDownOpen = true;
			}
		}, DispatcherPriority.Input);
	}

	private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
	{
		if (parent == null)
		{
			return null;
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is T result)
			{
				return result;
			}
			T val = FindVisualChild<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	private void PropertiesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
	{
		if (e.EditAction != DataGridEditAction.Commit)
		{
			return;
		}
		SceneObject liveObj = _liveObj;
		if (liveObj == null || !(e.Row.Item is PropertyRow propertyRow))
		{
			return;
		}
		string text = ((e.EditingElement is System.Windows.Controls.TextBox textBox) ? textBox.Text : propertyRow.Value);
		List<SceneObject> list = EditTargets(liveObj, propertyRow);
		SceneSnapshot sceneSnapshot = ((list.Count > 1) ? CaptureState(propertyRow.Name) : null);
		if (sceneSnapshot != null)
		{
			sceneSnapshot.MergeKey = propertyRow.Name + "#bulk";
		}
		bool flag = true;
		foreach (SceneObject item in list)
		{
			flag &= ((propertyRow.TextureIndex >= 0) ? ApplyTextureEdit(item, propertyRow.TextureIndex, propertyRow.Name, text) : ((propertyRow.DecalIndex >= 0) ? ApplyDecalEdit(item, propertyRow.DecalIndex, propertyRow.Name, text) : ((propertyRow.TextIndex >= 0) ? ApplyTextEdit(item, propertyRow.TextIndex, propertyRow.Name, text) : ApplyPropertyEdit(item, propertyRow.Name, text))));
		}
		if (sceneSnapshot != null)
		{
			PushCaptured(sceneSnapshot, !propertyRow.IsBoolean);
		}
		if (!flag)
		{
			Log($"Invalid {propertyRow.Name}: kept {propertyRow.Value}.");
		}
		if (propertyRow.Name == "Name")
		{
			SyncNodeName(liveObj);
		}
		bool flag2;
		switch (propertyRow.Name)
		{
		case "Direction":
		case "Speed":
		case "Speeding Mode":
			flag2 = true;
			break;
		default:
			flag2 = false;
			break;
		}
		if (flag2 && !liveObj.HasVelocityConfig)
		{
			SceneObject live = liveObj;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_liveRows = ObjectProperties(live);
				SetPropertyRows(_liveRows);
			}, DispatcherPriority.Background);
		}
		if (flag)
		{
			if (list.Count > 1)
			{
				foreach (SceneObject item2 in list)
				{
					SyncBulkBody(item2, propertyRow.Name);
				}
				Log($"Applied {propertyRow.Name} to {list.Count} parts.");
				StatusText.Text = $"{propertyRow.Name} ? {list.Count} parts";
			}
			else if (_physics.Simulating)
			{
				switch (propertyRow.Name)
				{
				case "Position":
					_physics.Teleport(liveObj);
					break;
				case "Rotation":
					liveObj.ComposeOrientation();
					_physics.Teleport(liveObj);
					break;
				case "Anchored":
				case "Mass":
				case "Size":
				case "Shape":
				case "Collision fidelity":
					_physics.RecreateBody(liveObj);
					break;
				case "CanCollide":
					if (liveObj.CanCollide)
					{
						_physics.RecreateBody(liveObj);
					}
					else
					{
						_physics.RemoveBody(liveObj);
					}
					break;
				}
			}
			switch (propertyRow.Name)
			{
			case "Position":
			case "Size":
			case "Rotation":
			case "Shape":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			if (flag2)
			{
				_scene.InvalidateShadows();
			}
			_needsFrame = true;
		}
		base.Dispatcher.BeginInvoke(new Action(UpdateLiveProps), DispatcherPriority.Background);
	}

	private void PropertyText_LostFocus(object sender, RoutedEventArgs e)
	{
		if (!_propFilterSync && sender is FrameworkElement { DataContext: PropertyRow dataContext })
		{
			CommitProperty(dataContext);
		}
	}

	private void PropertyText_KeyDown(object sender, KeyEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: PropertyRow dataContext })
		{
			if (e.Key == Key.Return)
			{
				CommitProperty(dataContext);
				e.Handled = true;
			}
			else if (e.Key == Key.Escape)
			{
				UpdateLiveProps();
				e.Handled = true;
			}
		}
	}

	private void PropertyBoolean_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: PropertyRow dataContext })
		{
			CommitProperty(dataContext);
		}
	}

	private void PropertyChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_propFilterSync && base.IsLoaded && !(sender is System.Windows.Controls.ComboBox { IsDropDownOpen: false, IsKeyboardFocusWithin: false }) && sender is FrameworkElement { DataContext: PropertyRow dataContext } && e.AddedItems.Count > 0)
		{
			CommitProperty(dataContext);
		}
	}

	private void PropertyImage_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { DataContext: PropertyRow dataContext }))
		{
			return;
		}
		SceneObject liveObj = _liveObj;
		if (liveObj == null)
		{
			return;
		}
		bool flag;
		switch (dataContext.Name)
		{
		case "Image":
		case "Face":
		case "Sound":
		case "Mesh file":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return;
		}
		if (dataContext.Name == "Mesh file")
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Replace mesh on " + liveObj.Name,
				Filter = "3D models|*.fbx;*.obj|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(this) == true)
			{
				PushUndo("Mesh file", mergeable: false);
				_scene.ClearMeshCache(liveObj.MeshPath);
				liveObj.MeshPath = openFileDialog.FileName;
				dataContext.Value = openFileDialog.FileName;
				Log("Mesh replaced on " + liveObj.Name + ": " + openFileDialog.FileName);
				StatusText.Text = "Mesh replaced";
				MarkDirty();
				_needsFrame = true;
			}
			return;
		}
		if (dataContext.Name == "Sound")
		{
			OpenFileDialog openFileDialog2 = new OpenFileDialog
			{
				Title = "Replace sound on " + liveObj.Name,
				Filter = "Audio files|*.wav;*.mp3;*.aiff;*.aif|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog2.ShowDialog(this) == true)
			{
				PushUndo("Sound", mergeable: false);
				AudioEngine.PartSoundStop(liveObj);
				liveObj.SoundPath = openFileDialog2.FileName;
				liveObj.SoundLoadedPath = null;
				dataContext.Value = openFileDialog2.FileName;
				Log("Sound replaced on " + liveObj.Name + ": " + openFileDialog2.FileName);
				StatusText.Text = "Sound replaced";
				MarkDirty();
				_needsFrame = true;
			}
			return;
		}
		OpenFileDialog openFileDialog3 = new OpenFileDialog
		{
			Title = ((dataContext.Name == "Face") ? "Choose NPC face image" : "Import decal image"),
			Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
			CheckFileExists = true
		};
		if (openFileDialog3.ShowDialog(this) != true)
		{
			return;
		}
		if (dataContext.Name == "Face")
		{
			PushUndo("NPC face", mergeable: false);
			liveObj.NpcFaceImage = openFileDialog3.FileName;
			dataContext.Value = openFileDialog3.FileName;
			dataContext.Thumbnail = LoadThumbnail(openFileDialog3.FileName);
			Log("Face set on " + liveObj.Name + ": " + openFileDialog3.FileName);
			StatusText.Text = "NPC face set";
			MarkDirty();
			_needsFrame = true;
			return;
		}
		if (dataContext.TextureIndex >= 0)
		{
			if (dataContext.TextureIndex >= liveObj.Textures.Count)
			{
				return;
			}
			PushUndo("Texture", mergeable: false);
			liveObj.Textures[dataContext.TextureIndex].Image = openFileDialog3.FileName;
			dataContext.Value = openFileDialog3.FileName;
			dataContext.Thumbnail = LoadThumbnail(openFileDialog3.FileName);
			Log($"Texture {dataContext.TextureIndex + 1} image replaced on {liveObj.Name}: {openFileDialog3.FileName}");
		}
		else if (dataContext.DecalIndex >= 0)
		{
			if (dataContext.DecalIndex >= liveObj.Decals.Count)
			{
				return;
			}
			PushUndo("Decal", mergeable: false);
			liveObj.Decals[dataContext.DecalIndex].Image = openFileDialog3.FileName;
			dataContext.Value = openFileDialog3.FileName;
			dataContext.Thumbnail = LoadThumbnail(openFileDialog3.FileName);
			Log($"Decal {dataContext.DecalIndex + 1} image replaced on {liveObj.Name}: {openFileDialog3.FileName}");
		}
		else
		{
			PushUndo("Decal", mergeable: false);
			liveObj.DecalImage = openFileDialog3.FileName;
			dataContext.Value = openFileDialog3.FileName;
			dataContext.Thumbnail = LoadThumbnail(openFileDialog3.FileName);
			_liveRows = ObjectProperties(liveObj);
			SetPropertyRows(_liveRows);
			Log("Decal applied to " + liveObj.Name + ": " + openFileDialog3.FileName);
		}
		StatusText.Text = "Decal applied";
		MarkDirty();
		_needsFrame = true;
	}

	private void PropertyAction_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { DataContext: PropertyRow dataContext }))
		{
			return;
		}
		SceneObject o = _liveObj;
		if (o == null)
		{
			return;
		}
		if (dataContext.Name == "Dialogue")
		{
			DialogueEditorWindow dialogueEditorWindow = new DialogueEditorWindow(o.NpcDialogue)
			{
				Owner = this
			};
			if (dialogueEditorWindow.ShowDialog() == true)
			{
				PushUndo("NPC dialogue", mergeable: false);
				o.NpcDialogue = dialogueEditorWindow.Dialogue;
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				MarkDirty();
				StatusText.Text = "NPC dialogue saved";
			}
		}
		else if (dataContext.Name == "Edit script")
		{
			ScriptEditorWindow scriptEditorWindow = new ScriptEditorWindow(o.Script ?? "", o.Name)
			{
				Owner = this
			};
			if (scriptEditorWindow.ShowDialog() == true && !(scriptEditorWindow.Code == (o.Script ?? "")))
			{
				PushUndo("Script", mergeable: false);
				o.Script = scriptEditorWindow.Code;
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				MarkDirty();
				StatusText.Text = (string.IsNullOrWhiteSpace(o.Script) ? "Script cleared" : "Script saved");
			}
		}
		else if (dataContext.Name == "Waypoints")
		{
			if (_waypointWin != null)
			{
				_waypointWin.Activate();
				return;
			}
			_waypointWin = new WaypointEditorWindow(o);
			_waypointWin.Owner = this;
			_waypointWin.Closed += delegate
			{
				WaypointEditorWindow waypointWin = _waypointWin;
				_waypointWin = null;
				if (waypointWin != null && waypointWin.Accepted && _scene.Objects.Contains(o))
				{
					PushUndo("Waypoints", mergeable: false);
					o.Waypoints.Clear();
					o.Waypoints.AddRange(waypointWin.Waypoints);
					if (_liveObj == o)
					{
						_liveRows = ObjectProperties(o);
						SetPropertyRows(_liveRows);
					}
					UpdateExplorerSelection();
					_needsFrame = true;
					MarkDirty();
					StatusText.Text = ((o.Waypoints.Count == 0) ? "Waypoints cleared" : $"{o.Waypoints.Count} waypoint(s) saved");
				}
			};
			_waypointWin.Show();
		}
		else if (dataContext.Name == "Add waypoint")
		{
			if (o.Waypoints.Count >= 16)
			{
				StatusText.Text = $"Max {16} waypoints";
				return;
			}
			PushUndo("Add waypoint", mergeable: false);
			o.Waypoints.Add(o.Position);
			_scene.SelWaypointObj = o;
			_scene.SelWaypointIndex = o.Waypoints.Count - 1;
			UpdateExplorerSelection();
			SelectWaypointNode(o, o.Waypoints.Count - 1);
			_needsFrame = true;
			MarkDirty();
			StatusText.Text = $"Waypoint {o.Waypoints.Count} added and selected";
		}
		else if (dataContext.Name == "Remove waypoint")
		{
			int selWaypointIndex = _scene.SelWaypointIndex;
			if (_scene.SelWaypointObj != o || selWaypointIndex < 0 || selWaypointIndex >= o.Waypoints.Count)
			{
				StatusText.Text = "Select a waypoint ball first";
				return;
			}
			PushUndo("Remove waypoint", mergeable: false);
			o.Waypoints.RemoveAt(selWaypointIndex);
			_scene.SelWaypointObj = null;
			_scene.SelWaypointIndex = -1;
			UpdateExplorerSelection();
			_needsFrame = true;
			MarkDirty();
			StatusText.Text = "Waypoint removed";
		}
		else if (dataContext.Name == "Add decal")
		{
			o.MigrateSingleToList();
			if (o.Decals.Count >= 6)
			{
				StatusText.Text = $"Max {6} decals per part";
				Log($"Decal: {o.Name} already has {o.Decals.Count} (max). Remove one first.");
				return;
			}
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = $"Import decal image ({o.Decals.Count + 1} of {6})",
				Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(this) == true)
			{
				PushUndo("Add decal", mergeable: false);
				o.Decals.Add(new DecalLayer
				{
					Image = openFileDialog.FileName,
					Face = "Front"
				});
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				_needsFrame = true;
				Log($"Decal {o.Decals.Count} applied to {o.Name}: {openFileDialog.FileName}");
				StatusText.Text = "Decal applied";
				MarkDirty();
			}
		}
		else if (dataContext.Name == "Add texture")
		{
			if (o.Textures.Count >= 4)
			{
				StatusText.Text = $"Max {4} textures per part";
				Log($"Texture: {o.Name} already has {o.Textures.Count} (max). Remove one first.");
				return;
			}
			OpenFileDialog openFileDialog2 = new OpenFileDialog
			{
				Title = $"Import texture image ({o.Textures.Count + 1} of {4})",
				Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog2.ShowDialog(this) == true)
			{
				PushUndo("Add texture", mergeable: false);
				o.Textures.Add(new TextureLayer
				{
					Image = openFileDialog2.FileName,
					Face = "Front"
				});
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				_needsFrame = true;
				Log($"Texture {o.Textures.Count} applied to {o.Name}: {openFileDialog2.FileName}");
				StatusText.Text = "Texture applied";
				MarkDirty();
			}
		}
		else if (dataContext.Name == "Remove" && (dataContext.DecalIndex >= 0 || dataContext.TextIndex >= 0 || dataContext.TextureIndex >= 0))
		{
			if (dataContext.TextureIndex >= 0)
			{
				if (dataContext.TextureIndex >= o.Textures.Count)
				{
					return;
				}
				PushUndo("Remove texture", mergeable: false);
				o.Textures.RemoveAt(dataContext.TextureIndex);
				Log($"Texture {dataContext.TextureIndex + 1} removed from {o.Name} ({o.Textures.Count} left).");
			}
			else if (dataContext.DecalIndex >= 0)
			{
				if (dataContext.DecalIndex >= o.Decals.Count)
				{
					return;
				}
				PushUndo("Remove decal", mergeable: false);
				o.Decals.RemoveAt(dataContext.DecalIndex);
				Log($"Decal {dataContext.DecalIndex + 1} removed from {o.Name} ({o.Decals.Count} left).");
			}
			else
			{
				if (dataContext.TextIndex >= o.Texts.Count)
				{
					return;
				}
				PushUndo("Remove text", mergeable: false);
				o.Texts.RemoveAt(dataContext.TextIndex);
				Log($"Text {dataContext.TextIndex + 1} removed from {o.Name} ({o.Texts.Count} left).");
			}
			_liveRows = ObjectProperties(o);
			SetPropertyRows(_liveRows);
			_needsFrame = true;
			MarkDirty();
			StatusText.Text = "Removed";
		}
		else if (dataContext.Name == "Remove" && dataContext.DecalIndex < 0 && dataContext.TextIndex < 0)
		{
			if (!string.IsNullOrWhiteSpace(o.DecalImage))
			{
				PushUndo("Remove decal", mergeable: false);
				o.DecalImage = null;
				o.DecalTransparency = 0f;
				o.DecalBlur = 0f;
				o.DecalFace = "Front";
				Log("Decal removed from " + o.Name + ".");
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				_needsFrame = true;
				MarkDirty();
				StatusText.Text = "Removed";
			}
		}
		else if (dataContext.Name == "Add sound")
		{
			OpenFileDialog openFileDialog3 = new OpenFileDialog
			{
				Title = "Attach sound to " + o.Name,
				Filter = "Audio files|*.wav;*.mp3;*.aiff;*.aif|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog3.ShowDialog(this) == true)
			{
				PushUndo("Add sound", mergeable: false);
				o.SoundPath = openFileDialog3.FileName;
				o.SoundLoadedPath = null;
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				Log("Sound added to " + o.Name + ": " + openFileDialog3.FileName);
				StatusText.Text = "Sound added";
				MarkDirty();
			}
		}
		else if (dataContext.Name == "Remove sound")
		{
			if (o.HasSound)
			{
				PushUndo("Remove sound", mergeable: false);
				AudioEngine.PartSoundStop(o);
				o.SoundPath = null;
				o.SoundPlaying = false;
				o.SoundLooped = false;
				Log("Sound removed from " + o.Name + ".");
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				MarkDirty();
				StatusText.Text = "Removed";
			}
		}
		else if (dataContext.Name == "Remove velocity")
		{
			if (o.HasVelocityConfig)
			{
				PushUndo("Remove velocity", mergeable: false);
				o.VelocityDirection = OpenTK.Mathematics.Vector3.Zero;
				o.VelocitySpeed = 0f;
				o.VelocityMode = SpeedMode.Accelerating;
				Log("Velocity removed from " + o.Name + ".");
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				_needsFrame = true;
				MarkDirty();
				StatusText.Text = "Removed";
			}
		}
		else if (dataContext.Name == "Add text")
		{
			if (o.Texts.Count >= 6)
			{
				StatusText.Text = $"Max {6} texts per part";
				Log($"Text: {o.Name} already has {o.Texts.Count} (max). Remove one first.");
			}
			else
			{
				PushUndo("Add text", mergeable: false);
				o.Texts.Add(new TextLayer
				{
					Text = "Text",
					Face = "Front"
				});
				_liveRows = ObjectProperties(o);
				SetPropertyRows(_liveRows);
				_needsFrame = true;
				MarkDirty();
				Log($"Text {o.Texts.Count} added to {o.Name}.");
				StatusText.Text = "Text added";
			}
		}
	}

	private void PropertySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!_propFilterSync && base.IsLoaded && sender is FrameworkElement { DataContext: PropertyRow dataContext } && _liveObj != null && !(dataContext.Type != "number") && !(sender is Slider { IsMouseCaptureWithin: false, IsKeyboardFocusWithin: false }))
		{
			CommitProperty(dataContext);
		}
	}

	private void PropertySwatch_Click(object sender, MouseButtonEventArgs e)
	{
		if (_scene.Selected == null)
		{
			Log("Color: select a part first.");
			StatusText.Text = "Select a part first";
			return;
		}
		if (sender is UIElement placementTarget)
		{
			ColorPopup.PlacementTarget = placementTarget;
		}
		SyncCustomEditor(_scene.Selected.Color);
		base.Dispatcher.BeginInvoke((Func<bool>)(() => ColorPopup.IsOpen = true), DispatcherPriority.Background);
	}

	private List<SceneObject> EditTargets(SceneObject live, PropertyRow row)
	{
		if (live.Shape == ShapeKind.None && live.Light != LightKind.None)
		{
			return new List<SceneObject> { live };
		}
		if (row.DecalIndex >= 0 || row.TextIndex >= 0 || row.TextureIndex >= 0 || !BulkEditRows.Contains(row.Name))
		{
			return new List<SceneObject> { live };
		}
		List<SceneObject> list = (from t in DragTargets()
			where t.Shape != ShapeKind.None || t.Light == LightKind.None
			select t).ToList();
		if (list.Count <= 0)
		{
			return new List<SceneObject> { live };
		}
		return list;
	}

	private void SyncBulkBody(SceneObject t, string name)
	{
		if (!_physics.Simulating)
		{
			return;
		}
		if ((name == "Anchored" || name == "Mass") ? true : false)
		{
			_physics.RecreateBody(t);
		}
		else if (name == "CanCollide")
		{
			if (t.CanCollide)
			{
				_physics.RecreateBody(t);
			}
			else
			{
				_physics.RemoveBody(t);
			}
		}
	}

	private void MarkVaryingRows()
	{
		SceneObject liveObj = _liveObj;
		if (liveObj == null || _liveRows == null || (liveObj.Shape == ShapeKind.None && liveObj.Light != LightKind.None))
		{
			return;
		}
		List<SceneObject> list = (from t in DragTargets()
			where t.Shape != ShapeKind.None || t.Light == LightKind.None
			select t).ToList();
		if (list.Count <= 1)
		{
			return;
		}
		foreach (PropertyRow r in _liveRows)
		{
			if (r.DecalIndex < 0 && r.TextIndex < 0 && r.TextureIndex < 0 && BulkEditRows.Contains(r.Name) && !r.IsBoolean && !r.IsChoice)
			{
				string first = DisplayRowValue(list[0], r);
				if (list.Any((SceneObject t) => DisplayRowValue(t, r) != first))
				{
					r.Value = "varies";
				}
			}
		}
	}

	private static void SetSoundPlaying(SceneObject o, bool playing)
	{
		o.SoundPlaying = playing;
	}

	private string? SoundBaseDir()
	{
		try
		{
			return string.IsNullOrEmpty(_savePath) ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_savePath));
		}
		catch
		{
			return null;
		}
	}

	private void CommitProperty(PropertyRow row)
	{
		if (_liveObj == null && _liveService != null)
		{
			CommitServiceProperty(row);
			return;
		}
		SceneObject liveObj = _liveObj;
		if (liveObj != null && liveObj.Shape == ShapeKind.None && liveObj.Light != LightKind.None)
		{
			CommitLightProperty(row);
			return;
		}
		SceneObject liveObj2 = _liveObj;
		bool flag = liveObj2 == null;
		string name;
		if (!flag)
		{
			name = row.Name;
			bool flag2 = ((name == "Class" || name == "Image") ? true : false);
			flag = flag2;
		}
		if (flag || row.Type == "file" || row.Type == "action")
		{
			return;
		}
		List<SceneObject> list = EditTargets(liveObj2, row);
		SceneSnapshot sceneSnapshot = CaptureState(row.Name);
		sceneSnapshot.MergeKey = ((list.Count > 1) ? (row.Name + "#bulk") : $"{row.Name}#{_scene.Objects.IndexOf(liveObj2)}");
		bool flag3 = true;
		foreach (SceneObject item in list)
		{
			flag3 &= ((row.TextureIndex >= 0) ? ApplyTextureEdit(item, row.TextureIndex, row.Name, row.Value) : ((row.DecalIndex >= 0) ? ApplyDecalEdit(item, row.DecalIndex, row.Name, row.Value) : ((row.TextIndex >= 0) ? ApplyTextEdit(item, row.TextIndex, row.Name, row.Value) : ApplyPropertyEdit(item, row.Name, row.Value))));
		}
		if (!flag3)
		{
			Log($"Invalid {row.Name}: kept {DisplayRowValue(liveObj2, row)}.");
			StatusText.Text = "Invalid " + row.Name;
			UpdateLiveProps();
			return;
		}
		PushCaptured(sceneSnapshot, !row.IsBoolean);
		if (row.Name == "Name")
		{
			SyncNodeName(liveObj2);
		}
		if (row.Name == "NPC Type")
		{
			_liveRows = ObjectProperties(liveObj2);
			SetPropertyRows(_liveRows);
		}
		if (row.Name == "Shape" || row.Name == "Material" || row.Name == "Effect")
		{
			_liveRows = ObjectProperties(liveObj2);
			SetPropertyRows(_liveRows);
		}
		if (row.Name == "Shape" && liveObj2.Shape == ShapeKind.Mesh && string.IsNullOrWhiteSpace(liveObj2.MeshPath))
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Import mesh for " + liveObj2.Name,
				Filter = "3D models|*.fbx;*.obj|All files|*.*",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(this) == true)
			{
				SceneSnapshot snap = CaptureState("Mesh file");
				_scene.ClearMeshCache(liveObj2.MeshPath);
				liveObj2.MeshPath = openFileDialog.FileName;
				PushCaptured(snap, mergeable: false);
				MarkDirty();
				Log("Mesh set on " + liveObj2.Name + ": " + openFileDialog.FileName);
			}
			_liveRows = ObjectProperties(liveObj2);
			SetPropertyRows(_liveRows);
		}
		switch (row.Name)
		{
		case "Direction":
		case "Speed":
		case "Speeding Mode":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag && !liveObj2.HasVelocityConfig)
		{
			_liveRows = ObjectProperties(liveObj2);
			SetPropertyRows(_liveRows);
		}
		name = row.Name;
		if ((name == "Spawn" || name == "TeleportLink") ? true : false)
		{
			SyncNodeIcon(liveObj2);
			SelectedLabel.Text = SelectionTitle();
		}
		if (row.Name == "Name")
		{
			SyncInspectorHeader();
		}
		MarkDirty();
		if (row.Name == "Color")
		{
			row.Swatch = ColorBrush(liveObj2.Color);
			SyncCustomEditor(liveObj2.Color);
		}
		if (list.Count > 1)
		{
			foreach (SceneObject item2 in list)
			{
				SyncBulkBody(item2, row.Name);
			}
			Log($"Applied {row.Name} to {list.Count} parts.");
			StatusText.Text = $"{row.Name} ? {list.Count} parts";
		}
		else if (_physics.Simulating)
		{
			switch (row.Name)
			{
			case "Size":
			case "Shape":
			case "Anchored":
			case "Mass":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				_physics.RecreateBody(liveObj2);
			}
			else if (row.Name == "CanCollide")
			{
				if (liveObj2.CanCollide)
				{
					_physics.RecreateBody(liveObj2);
				}
				else
				{
					_physics.RemoveBody(liveObj2);
				}
			}
			else
			{
				name = row.Name;
				if ((name == "Position" || name == "Rotation") ? true : false)
				{
					if (row.Name == "Rotation")
					{
						liveObj2.ComposeOrientation();
					}
					_physics.Teleport(liveObj2);
				}
			}
		}
		switch (row.Name)
		{
		case "Position":
		case "Size":
		case "Rotation":
		case "Shape":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			_scene.InvalidateShadows();
		}
		switch (row.Name)
		{
		case "Transparency":
		case "CastShadow":
		case "Material":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			_scene.InvalidateShadows();
		}
		if (row.Name == "Shape")
		{
			RefreshExplorerShape(liveObj2);
		}
		_needsFrame = true;
		UpdateLiveProps();
	}

	private static string DisplayRowValue(SceneObject o, PropertyRow row)
	{
		if (row.DecalIndex >= 0)
		{
			if (row.DecalIndex >= o.Decals.Count)
			{
				return "";
			}
			DecalLayer decalLayer = o.Decals[row.DecalIndex];
			return row.Name switch
			{
				"Face" => decalLayer.Face, 
				"Image Transparency" => decalLayer.Transparency.ToString("0.00", CultureInfo.InvariantCulture), 
				"Image Blur" => decalLayer.Blur.ToString("0.00", CultureInfo.InvariantCulture), 
				"Offset" => OffsetValue(decalLayer.OffsetX, decalLayer.OffsetY), 
				"Scale" => decalLayer.Scale.ToString("0.00", CultureInfo.InvariantCulture), 
				_ => "", 
			};
		}
		if (row.TextureIndex >= 0)
		{
			if (row.TextureIndex >= o.Textures.Count)
			{
				return "";
			}
			TextureLayer textureLayer = o.Textures[row.TextureIndex];
			return row.Name switch
			{
				"Texture Face" => textureLayer.Face, 
				"Texture Transparency" => textureLayer.Transparency.ToString("0.00", CultureInfo.InvariantCulture), 
				"Tiling X" => textureLayer.TilingX.ToString("0.00", CultureInfo.InvariantCulture), 
				"Tiling Y" => textureLayer.TilingY.ToString("0.00", CultureInfo.InvariantCulture), 
				"Tiling Offset X" => textureLayer.OffsetX.ToString("0.00", CultureInfo.InvariantCulture), 
				"Tiling Offset Y" => textureLayer.OffsetY.ToString("0.00", CultureInfo.InvariantCulture), 
				_ => "", 
			};
		}
		if (row.TextIndex >= 0)
		{
			if (row.TextIndex >= o.Texts.Count)
			{
				return "";
			}
			TextLayer textLayer = o.Texts[row.TextIndex];
			return row.Name switch
			{
				"Text" => textLayer.Text, 
				"Face" => textLayer.Face, 
				"Font" => textLayer.Font, 
				"Size" => textLayer.Size.ToString("0", CultureInfo.InvariantCulture), 
				"Color" => textLayer.Color, 
				"Text Transparency" => textLayer.Transparency.ToString("0.00", CultureInfo.InvariantCulture), 
				"Text Blur" => textLayer.Blur.ToString("0.00", CultureInfo.InvariantCulture), 
				"Bold" => textLayer.Bold.ToString(), 
				"Outline" => textLayer.Outline.ToString("0.0", CultureInfo.InvariantCulture), 
				"Outline Color" => textLayer.OutlineColor, 
				"Offset" => OffsetValue(textLayer.OffsetX, textLayer.OffsetY), 
				"Scale" => textLayer.Scale.ToString("0.00", CultureInfo.InvariantCulture), 
				_ => "", 
			};
		}
		return row.Name switch
		{
			"Name" => o.Name, 
			"Position" => PosValue(o), 
			"Size" => SizeValue(o), 
			"Rotation" => RotValue(o), 
			"Shape" => o.Shape.ToString(), 
			"Color" => ColorValue(o), 
			"Material" => o.Material.ToString(), 
			"Transparency" => o.Transparency.ToString("0.00", CultureInfo.InvariantCulture), 
			"Reflectance" => o.Reflectance.ToString("0.00", CultureInfo.InvariantCulture), 
			"Reflection" => o.Reflectance.ToString("0.00", CultureInfo.InvariantCulture), 
			"CastShadow" => o.CastShadow.ToString(), 
			"Anchored" => o.Anchored.ToString(), 
			"Locked" => o.Locked.ToString(), 
			"Spawn" => o.IsSpawn.ToString(), 
			"Effect" => o.ParticlePreset, 
			"Mass" => o.Mass.ToString("0.00", CultureInfo.InvariantCulture), 
			"Damage" => o.Damage.ToString("0.00", CultureInfo.InvariantCulture), 
			"Tiling" => o.Tiling.ToString("0.00", CultureInfo.InvariantCulture), 
			"Color Brightness" => o.ColorBrightness.ToString("0.00", CultureInfo.InvariantCulture), 
			"Rate" => o.EmissionRate.ToString("0.00", CultureInfo.InvariantCulture), 
			"Lifetime" => o.ParticleLifetime.ToString("0.00", CultureInfo.InvariantCulture), 
			"Particle speed" => o.ParticleSpeed.ToString("0.00", CultureInfo.InvariantCulture), 
			"Particle size" => o.ParticleSize.ToString("0.00", CultureInfo.InvariantCulture), 
			"Spread" => o.ParticleSpread.ToString("0.00", CultureInfo.InvariantCulture), 
			"Gravity" => o.ParticleGravity.ToString("0.00", CultureInfo.InvariantCulture), 
			"TeleportLink" => o.TeleportLink, 
			"CanCollide" => o.CanCollide.ToString(), 
			"Hidden time" => o.TimedHidden.ToString("0.00", CultureInfo.InvariantCulture), 
			"Timed offset" => o.TimedOffset.ToString("0.00", CultureInfo.InvariantCulture), 
			"Visible time" => o.TimedVisible.ToString("0.00", CultureInfo.InvariantCulture), 
			"Bounce power" => o.BouncePower.ToString("0.00", CultureInfo.InvariantCulture), 
			"Magnet power" => o.MagnetPower.ToString("0.00", CultureInfo.InvariantCulture), 
			"Magnet range" => o.MagnetRange.ToString("0.00", CultureInfo.InvariantCulture), 
			"Conveyor direction" => string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", o.ConveyorDirection.X, o.ConveyorDirection.Y, o.ConveyorDirection.Z), 
			"Conveyor speed" => o.ConveyorSpeed.ToString("0.00", CultureInfo.InvariantCulture), 
			"Move distance" => o.MoveDistance.ToString("0.00", CultureInfo.InvariantCulture), 
			"Move speed" => o.MoveSpeed.ToString("0.00", CultureInfo.InvariantCulture), 
			"Spin speed" => o.SpinSpeed.ToString("0.00", CultureInfo.InvariantCulture), 
			"Hits to break" => o.BreakHits.ToString(CultureInfo.InvariantCulture), 
			"Direction" => string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", o.VelocityDirection.X, o.VelocityDirection.Y, o.VelocityDirection.Z), 
			"Speed" => o.VelocitySpeed.ToString("0.00", CultureInfo.InvariantCulture), 
			"Speeding Mode" => (o.VelocityMode == SpeedMode.Accelerating) ? "Accelerating" : "Always/Stable", 
			"NPC Type" => o.NpcType.ToString(), 
			"NPC Damage" => o.NpcDamage.ToString("0.0", CultureInfo.InvariantCulture), 
			"NPC Speed" => o.NpcSpeed.ToString("0.0", CultureInfo.InvariantCulture), 
			"Dialogue" => o.NpcDialogue, 
			"Chat Range" => o.NpcChatRange.ToString("0.0", CultureInfo.InvariantCulture), 
			"Chat Pause" => o.NpcChatInterval.ToString("0.0", CultureInfo.InvariantCulture), 
			_ => "", 
		};
	}

	private void RefreshExplorerShape(SceneObject o)
	{
		SyncNodeIcon(o);
		RefreshExplorer();
		ExplorerTree.UpdateLayout();
		SelectNodeRecursive(ExplorerTree, o);
	}

	private static bool ApplyTextEdit(SceneObject o, int index, string name, string text)
	{
		if (index < 0 || index >= o.Texts.Count)
		{
			return false;
		}
		TextLayer textLayer = o.Texts[index];
		switch (name)
		{
		case "Text":
			if (string.IsNullOrEmpty(text) || text.Length > 200)
			{
				return false;
			}
			textLayer.Text = text;
			return true;
		case "Face":
		{
			bool flag;
			switch (text.Trim())
			{
			case "Front":
			case "Back":
			case "Left":
			case "Right":
			case "Top":
			case "Bottom":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				return false;
			}
			textLayer.Face = text.Trim();
			return true;
		}
		case "Font":
			if (!TextFonts.Exists(text))
			{
				return false;
			}
			textLayer.Font = text.Trim();
			return true;
		case "Size":
		{
			if (!TryFloat(text, out var f2))
			{
				return false;
			}
			textLayer.Size = Math.Clamp(f2, 8f, 256f);
			return true;
		}
		case "Color":
		{
			if (!PartColor.TryParseRgb(text, out var color) && !PartColor.TryParseHex(text, out color))
			{
				return false;
			}
			textLayer.Color = PartColor.ToRgbText(color);
			return true;
		}
		case "Text Transparency":
		{
			if (!TryFloat(text, out var f5))
			{
				return false;
			}
			textLayer.Transparency = Math.Clamp(f5, 0f, 1f);
			return true;
		}
		case "Text Blur":
		{
			if (!TryFloat(text, out var f3))
			{
				return false;
			}
			textLayer.Blur = Math.Clamp(f3, 0f, 1f);
			return true;
		}
		case "Bold":
		{
			if (!bool.TryParse(text.Trim(), out var result))
			{
				return false;
			}
			textLayer.Bold = result;
			return true;
		}
		case "Outline":
		{
			if (!TryFloat(text, out var f4))
			{
				return false;
			}
			textLayer.Outline = Math.Clamp(f4, 0f, 16f);
			return true;
		}
		case "Outline Color":
		{
			if (!PartColor.TryParseRgb(text, out var color2) && !PartColor.TryParseHex(text, out color2))
			{
				return false;
			}
			textLayer.OutlineColor = PartColor.ToRgbText(color2);
			return true;
		}
		case "Offset":
		{
			if (!TryV2(text, out var x, out var y))
			{
				return false;
			}
			textLayer.OffsetX = Math.Clamp(x, -1f, 1f);
			textLayer.OffsetY = Math.Clamp(y, -1f, 1f);
			return true;
		}
		case "Scale":
		{
			if (!TryFloat(text, out var f))
			{
				return false;
			}
			textLayer.Scale = Math.Clamp(f, 0.05f, 1f);
			return true;
		}
		default:
			return false;
		}
	}

	private static bool ApplyTextureEdit(SceneObject o, int index, string name, string text)
	{
		if (index < 0 || index >= o.Textures.Count)
		{
			return false;
		}
		TextureLayer textureLayer = o.Textures[index];
		switch (name)
		{
		case "Texture Face":
		{
			bool flag;
			switch (text.Trim())
			{
			case "Front":
			case "Back":
			case "Left":
			case "Right":
			case "Top":
			case "Bottom":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				return false;
			}
			textureLayer.Face = text.Trim();
			return true;
		}
		case "Texture Transparency":
		{
			if (!TryFloat(text, out var f3))
			{
				return false;
			}
			textureLayer.Transparency = Math.Clamp(f3, 0f, 1f);
			return true;
		}
		case "Tiling X":
		{
			if (!TryFloat(text, out var f5))
			{
				return false;
			}
			textureLayer.TilingX = Math.Clamp(f5, 0.1f, 32f);
			return true;
		}
		case "Tiling Y":
		{
			if (!TryFloat(text, out var f2))
			{
				return false;
			}
			textureLayer.TilingY = Math.Clamp(f2, 0.1f, 32f);
			return true;
		}
		case "Tiling Offset X":
		{
			if (!TryFloat(text, out var f4))
			{
				return false;
			}
			textureLayer.OffsetX = Math.Clamp(f4, -10f, 10f);
			return true;
		}
		case "Tiling Offset Y":
		{
			if (!TryFloat(text, out var f))
			{
				return false;
			}
			textureLayer.OffsetY = Math.Clamp(f, -10f, 10f);
			return true;
		}
		default:
			return false;
		}
	}

	private static bool ApplyDecalEdit(SceneObject o, int index, string name, string text)
	{
		if (index < 0 || index >= o.Decals.Count)
		{
			return false;
		}
		DecalLayer decalLayer = o.Decals[index];
		switch (name)
		{
		case "Face":
		{
			bool flag;
			switch (text.Trim())
			{
			case "Front":
			case "Back":
			case "Left":
			case "Right":
			case "Top":
			case "Bottom":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				return false;
			}
			decalLayer.Face = text.Trim();
			return true;
		}
		case "Image Transparency":
		{
			if (!TryFloat(text, out var f3))
			{
				return false;
			}
			decalLayer.Transparency = Math.Clamp(f3, 0f, 1f);
			return true;
		}
		case "Image Blur":
		{
			if (!TryFloat(text, out var f2))
			{
				return false;
			}
			decalLayer.Blur = Math.Clamp(f2, 0f, 1f);
			return true;
		}
		case "Offset":
		{
			if (!TryV2(text, out var x, out var y))
			{
				return false;
			}
			decalLayer.OffsetX = Math.Clamp(x, -1f, 1f);
			decalLayer.OffsetY = Math.Clamp(y, -1f, 1f);
			return true;
		}
		case "Scale":
		{
			if (!TryFloat(text, out var f))
			{
				return false;
			}
			decalLayer.Scale = Math.Clamp(f, 0.05f, 1f);
			return true;
		}
		default:
			return false;
		}
	}

	private bool ApplyServiceEdit(PropertyRow row)
	{
		if (_liveService == "Sun")
		{
			switch (row.Name)
			{
			case "Direction":
			{
				if (!TryV3(row.Value, out var v) || v.LengthSquared < 1E-08f)
				{
					return false;
				}
				_scene.SunDirection = v;
				return true;
			}
			case "Color":
			{
				if (!PartColor.TryParseRgb(row.Value, out var color) && !PartColor.TryParseHex(row.Value, out color))
				{
					return false;
				}
				_scene.SunColor = new OpenTK.Mathematics.Vector3((float)(int)color.R / 255f, (float)(int)color.G / 255f, (float)(int)color.B / 255f);
				return true;
			}
			case "Intensity":
			{
				if (!TryFloat(row.Value, out var f))
				{
					return false;
				}
				_scene.SunIntensity = Math.Clamp(f, 0f, 2f);
				return true;
			}
			case "Shadows":
			{
				if (bool.TryParse(row.Value.Trim(), out var result))
				{
					ApplyShadows(result);
					return true;
				}
				if (row.Value.Trim() == "1")
				{
					ApplyShadows(on: true);
					return true;
				}
				if (row.Value.Trim() == "0")
				{
					ApplyShadows(on: false);
					return true;
				}
				return false;
			}
			}
		}
		else if (_liveService == "Atmosphere")
		{
			switch (row.Name)
			{
			case "Fog Density":
			{
				if (!TryFloat(row.Value, out var f2))
				{
					return false;
				}
				ApplyFog(Math.Clamp(f2, 0f, 0.05f));
				return true;
			}
			case "Fog Color":
			{
				if (!PartColor.TryParseRgb(row.Value, out var color3) && !PartColor.TryParseHex(row.Value, out color3))
				{
					return false;
				}
				_scene.FogColor = new OpenTK.Mathematics.Vector3((float)(int)color3.R / 255f, (float)(int)color3.G / 255f, (float)(int)color3.B / 255f);
				return true;
			}
			case "Haze":
			{
				if (!TryFloat(row.Value, out var f3))
				{
					return false;
				}
				_scene.SkyHaze = Math.Clamp(f3, 0f, 0.05f);
				return true;
			}
			case "Sky Tint":
			{
				if (!PartColor.TryParseRgb(row.Value, out var color2) && !PartColor.TryParseHex(row.Value, out color2))
				{
					return false;
				}
				_scene.SkyTint = new OpenTK.Mathematics.Vector3((float)(int)color2.R / 255f, (float)(int)color2.G / 255f, (float)(int)color2.B / 255f);
				return true;
			}
			case "Sky Style":
			{
				if (!int.TryParse(row.Value.Trim(), out var result2))
				{
					return false;
				}
				_scene.SkyStyle = Math.Clamp(result2, 0, 2);
				return true;
			}
			}
		}
		else if (_liveService == "Lighting" && row.Name == "Ambient")
		{
			if (!TryFloat(row.Value, out var f4))
			{
				return false;
			}
			_scene.AmbientBoost = Math.Clamp(f4, 0f, 2f);
			return true;
		}
		return false;
	}

	private void CommitServiceProperty(PropertyRow row)
	{
		if (row.Type == "action")
		{
			return;
		}
		SceneSnapshot snap = CaptureState(row.Name);
		if (!ApplyServiceEdit(row))
		{
			Log($"Invalid {row.Name}: kept {DisplayServiceValue(row)}.");
			StatusText.Text = "Invalid " + row.Name;
			UpdateLiveProps();
			return;
		}
		if (row.Name == "Color")
		{
			row.Swatch = BrushOf(_scene.SunColor);
		}
		if (row.Name == "Fog Color")
		{
			row.Swatch = BrushOf(_scene.FogColor);
		}
		if (row.Name == "Sky Tint")
		{
			row.Swatch = BrushOf(_scene.SkyTint);
		}
		PushCaptured(snap, !row.IsBoolean);
		MarkDirty();
		_needsFrame = true;
		UpdateLiveProps();
	}

	private string DisplayServiceValue(PropertyRow row)
	{
		string liveService = _liveService;
		string name = row.Name;
		switch (liveService)
		{
		case "Sun":
			switch (name)
			{
			case "Direction":
				return DirValue(_scene.SunDirection);
			case "Color":
				return ColorValueVec(_scene.SunColor);
			case "Intensity":
				return _scene.SunIntensity.ToString("0.00", CultureInfo.InvariantCulture);
			case "Shadows":
				return _scene.ShadowsEnabled.ToString();
			}
			break;
		case "Atmosphere":
			switch (name)
			{
			case "Fog Density":
				return _scene.FogDensity.ToString("0.000", CultureInfo.InvariantCulture);
			case "Fog Color":
				return ColorValueVec(_scene.FogColor);
			case "Haze":
				return _scene.SkyHaze.ToString("0.000", CultureInfo.InvariantCulture);
			case "Sky Tint":
				return ColorValueVec(_scene.SkyTint);
			case "Sky Style":
				return _scene.SkyStyle.ToString(CultureInfo.InvariantCulture);
			}
			break;
		case "Lighting":
			if (!(name == "Ambient"))
			{
				break;
			}
			return _scene.AmbientBoost.ToString("0.00", CultureInfo.InvariantCulture);
		}
		return "";
	}

	private void UpdateServiceRow(PropertyRow r)
	{
		r.Value = DisplayServiceValue(r);
		if (r.Name == "Color")
		{
			r.Swatch = BrushOf(_scene.SunColor);
		}
		else if (r.Name == "Fog Color")
		{
			r.Swatch = BrushOf(_scene.FogColor);
		}
		else if (r.Name == "Sky Tint")
		{
			r.Swatch = BrushOf(_scene.SkyTint);
		}
	}

	private static string LightClass(SceneObject o)
	{
		return $"{o.Light}Light";
	}

	private static List<PropertyRow> LightProperties(SceneObject o)
	{
		return new List<PropertyRow>
		{
			new PropertyRow
			{
				Category = "General",
				Name = "Name",
				Value = o.Name,
				Description = "Light name. Shows in Explorer."
			},
			new PropertyRow
			{
				Category = "General",
				Name = "Class",
				Value = LightClass(o),
				Type = "readonly",
				Description = "Built-in class."
			},
			new PropertyRow
			{
				Category = "Light",
				Name = "Color",
				Type = "color",
				Value = ColorValue(o),
				Swatch = ColorBrush(o.Color),
				Description = "Light tint. RGB 0-255 (e.g. 255, 240, 220) or #hex."
			},
			new PropertyRow
			{
				Category = "Light",
				Name = "Brightness",
				Value = o.Brightness.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Light multiplier 0-20 (2 = default)."
			},
			new PropertyRow
			{
				Category = "Light",
				Name = "Range",
				Value = o.Range.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Reach in studs 1-100. Fades to nothing at the edge."
			}
		};
	}

	private void UpdateLightRow(PropertyRow r, SceneObject o)
	{
		switch (r.Name)
		{
		case "Name":
			r.Value = o.Name;
			break;
		case "Color":
			r.Value = ColorValue(o);
			r.Swatch = ColorBrush(o.Color);
			break;
		case "Brightness":
			r.Value = o.Brightness.ToString("0.00", CultureInfo.InvariantCulture);
			break;
		case "Range":
			r.Value = o.Range.ToString("0.00", CultureInfo.InvariantCulture);
			break;
		}
	}

	private string DisplayLightValue(SceneObject o, PropertyRow row)
	{
		return row.Name switch
		{
			"Name" => o.Name, 
			"Color" => ColorValue(o), 
			"Brightness" => o.Brightness.ToString("0.00", CultureInfo.InvariantCulture), 
			"Range" => o.Range.ToString("0.00", CultureInfo.InvariantCulture), 
			_ => "", 
		};
	}

	private static bool ApplyLightEdit(SceneObject o, string name, string text)
	{
		switch (name)
		{
		case "Name":
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			o.Name = text.Trim();
			return true;
		case "Color":
		{
			if (!PartColor.TryParseRgb(text, out var color) && !PartColor.TryParseHex(text, out color))
			{
				return false;
			}
			o.Color = PartColor.FromMediaColor(color);
			return true;
		}
		case "Brightness":
		{
			if (!TryFloat(text, out var f2))
			{
				return false;
			}
			o.Brightness = Math.Clamp(f2, 0f, 20f);
			return true;
		}
		case "Range":
		{
			if (!TryFloat(text, out var f))
			{
				return false;
			}
			o.Range = Math.Clamp(f, 1f, 100f);
			return true;
		}
		default:
			return false;
		}
	}

	private void CommitLightProperty(PropertyRow row)
	{
		SceneObject liveObj = _liveObj;
		if (liveObj == null)
		{
			return;
		}
		SceneSnapshot sceneSnapshot = CaptureState(row.Name);
		sceneSnapshot.MergeKey = $"{row.Name}#{_scene.Objects.IndexOf(liveObj)}";
		if (!ApplyLightEdit(liveObj, row.Name, row.Value))
		{
			Log($"Invalid {row.Name}: kept {DisplayLightValue(liveObj, row)}.");
			StatusText.Text = "Invalid " + row.Name;
			UpdateLiveProps();
			return;
		}
		PushCaptured(sceneSnapshot, !row.IsBoolean);
		if (row.Name == "Name")
		{
			SyncNodeName(liveObj);
		}
		if (row.Name == "Color")
		{
			row.Swatch = ColorBrush(liveObj.Color);
			SyncCustomEditor(liveObj.Color);
		}
		MarkDirty();
		// No depth invalidation: point lights don't cast (sun map only) and
		// glyphs never land in it, so light edits must not trigger a re-render.
		_needsFrame = true;
		UpdateLiveProps();
	}

	private List<PropertyRow> ServiceProperties(string service)
	{
		List<PropertyRow> list = ((service == "Sun") ? new List<PropertyRow>
		{
			new PropertyRow
			{
				Category = "Sun",
				Name = "Direction",
				Type = "vector",
				Value = "",
				Description = "Sun direction, e.g. 0.50, 0.80, 0.60. Retargets the key light, sky sun and shadow frustum."
			},
			new PropertyRow
			{
				Category = "Sun",
				Name = "Color",
				Type = "color",
				Value = "",
				Description = "Sunlight tint for parts and sky. RGB 0-255 (e.g. 255, 247, 235) or #hex."
			},
			new PropertyRow
			{
				Category = "Sun",
				Name = "Intensity",
				Value = "",
				Description = "Sunlight multiplier 0-2 (1 = default)."
			},
			new PropertyRow
			{
				Category = "Sun",
				Name = "Shadows",
				Type = "bool",
				Value = "",
				Description = "Sun shadows (same switch as View > Shadows)."
			}
		} : ((!(service == "Atmosphere")) ? new List<PropertyRow>
		{
			new PropertyRow
			{
				Category = "Lighting",
				Name = "Ambient",
				Value = "",
				Description = "Sky/ground ambient multiplier 0-2 (1 = default)."
			}
		} : new List<PropertyRow>
		{
			new PropertyRow
			{
				Category = "Atmosphere",
				Name = "Fog Density",
				Value = "",
				Description = "Exponential fog 0-0.05 (0 disables). Same as Settings > Fog."
			},
			new PropertyRow
			{
				Category = "Atmosphere",
				Name = "Fog Color",
				Type = "color",
				Value = "",
				Description = "Horizon haze and background clear color."
			},
			new PropertyRow
			{
				Category = "Atmosphere",
				Name = "Haze",
				Value = "",
				Description = "Sky haze (Mie scattering) 0-0.05. Higher = milkier horizon."
			},
			new PropertyRow
			{
				Category = "Atmosphere",
				Name = "Sky Tint",
				Type = "color",
				Value = "",
				Description = "Sky scattering tint. RGB 0-255 (e.g. 255, 255, 255) or #hex. White = natural."
			},
			new PropertyRow
			{
				Category = "Atmosphere",
				Name = "Sky Style",
				Value = "",
				Description = "Sky renderer 0-2: 0 realistic, 1 cartoon, 2 soft gradient."
			}
		}));
		List<PropertyRow> list2 = list;
		foreach (PropertyRow item in list2)
		{
			UpdateServiceRow(item);
		}
		return list2;
	}

	private static string DirValue(OpenTK.Mathematics.Vector3 v)
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:0.00}, {1:0.00}, {2:0.00}", v.X, v.Y, v.Z);
	}

	private static string ColorValueVec(OpenTK.Mathematics.Vector3 c)
	{
		return $"{ToByte(c.X)}, {ToByte(c.Y)}, {ToByte(c.Z)}";
	}

	private static System.Windows.Media.Brush BrushOf(OpenTK.Mathematics.Vector3 c)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(ToByte(c.X), ToByte(c.Y), ToByte(c.Z)));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static bool ApplyPropertyEdit(SceneObject o, string name, string text)
	{
		switch (name)
		{
		case "Name":
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			o.Name = text.Trim();
			return true;
		case "Position":
		{
			if (!TryV3(text, out var v5))
			{
				return false;
			}
			o.Position = v5;
			return true;
		}
		case "Size":
		{
			if (!TryV3(text, out var v3))
			{
				return false;
			}
			o.Size = new OpenTK.Mathematics.Vector3(Math.Max(v3.X, 0.01f), Math.Max(v3.Y, 0.01f), Math.Max(v3.Z, 0.01f));
			return true;
		}
		case "Rotation":
		{
			if (!TryV3(text.Replace("\u00B0", ""), out var v4))
			{
				return false;
			}
			o.Rotation = v4;
			o.ComposeOrientation();
			return true;
		}
		case "Shape":
		{
			if (!Enum.TryParse<ShapeKind>(text, out var result12))
			{
				return false;
			}
			o.Shape = result12;
			if (result12 == ShapeKind.Killbrick && o.Damage <= 0.01f)
			{
				o.Damage = 100f;
			}
			return true;
		}
		case "Color":
		{
			if (!PartColor.TryParseRgb(text, out var color) && !PartColor.TryParseHex(text, out color))
			{
				return false;
			}
			o.Color = PartColor.FromMediaColor(color);
			return true;
		}
		case "Anchored":
		{
			if (bool.TryParse(text.Trim(), out var result8))
			{
				o.Anchored = result8;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.Anchored = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.Anchored = false;
				return true;
			}
			return false;
		}
		case "Locked":
		{
			if (bool.TryParse(text.Trim(), out var result4))
			{
				o.Locked = result4;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.Locked = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.Locked = false;
				return true;
			}
			return false;
		}
		case "Spawn":
		{
			if (bool.TryParse(text.Trim(), out var result9))
			{
				o.IsSpawn = result9;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.IsSpawn = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.IsSpawn = false;
				return true;
			}
			return false;
		}
		case "Effect":
		{
			text = (text ?? "").Trim();
			bool flag;
			switch (text)
			{
			case "None":
			case "Custom":
			case "Fire":
			case "Smoke":
			case "Sparkles":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				return false;
			}
			o.ParticlePreset = text;
			o.IsEmitter = text != "None";
			if (text != "None" && text != "Custom")
			{
				ApplyParticlePreset(o, text);
			}
			return true;
		}
		case "Mass":
		{
			if (!TryFloat(text, out var f5))
			{
				return false;
			}
			o.Mass = Math.Clamp(f5, 0.01f, 1E+14f);
			return true;
		}
		case "Damage":
		{
			if (!TryFloat(text, out var f2))
			{
				return false;
			}
			o.Damage = Math.Clamp(f2, 0f, 1000f);
			return true;
		}
		case "Tiling":
		{
			if (!TryFloat(text, out var f21))
			{
				return false;
			}
			o.Tiling = Math.Clamp(f21, 0.1f, 8f);
			return true;
		}
		case "Color Brightness":
		{
			if (!TryFloat(text, out var f12))
			{
				return false;
			}
			o.ColorBrightness = Math.Clamp(f12, 0f, 50f);
			return true;
		}
		case "Rate":
		{
			if (!TryFloat(text, out var f14))
			{
				return false;
			}
			o.EmissionRate = Math.Clamp(f14, 0f, 200f);
			return true;
		}
		case "Lifetime":
		{
			if (!TryFloat(text, out var f7))
			{
				return false;
			}
			o.ParticleLifetime = Math.Clamp(f7, 0.1f, 10f);
			return true;
		}
		case "Particle speed":
		{
			if (!TryFloat(text, out var f3))
			{
				return false;
			}
			o.ParticleSpeed = Math.Clamp(f3, 0f, 50f);
			return true;
		}
		case "Particle size":
		{
			if (!TryFloat(text, out var f31))
			{
				return false;
			}
			o.ParticleSize = Math.Clamp(f31, 0.1f, 4f);
			return true;
		}
		case "Spread":
		{
			if (!TryFloat(text, out var f25))
			{
				return false;
			}
			o.ParticleSpread = Math.Clamp(f25, 0f, 1f);
			return true;
		}
		case "Gravity":
		{
			if (!TryFloat(text, out var f18))
			{
				return false;
			}
			o.ParticleGravity = Math.Clamp(f18, -20f, 20f);
			return true;
		}
		case "TeleportLink":
			text = (text ?? "").Trim();
			o.TeleportLink = ((text.Length > 24) ? text.Substring(0, 24) : text);
			return true;
		case "CanCollide":
		{
			if (bool.TryParse(text.Trim(), out var result5))
			{
				o.CanCollide = result5;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.CanCollide = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.CanCollide = false;
				return true;
			}
			return false;
		}
		case "Collision fidelity":
		{
			if (!Enum.TryParse<CollisionFidelityKind>(text.Trim(), out var result))
			{
				return false;
			}
			o.CollisionFidelity = result;
			return true;
		}
		case "Render fidelity":
		{
			if (!Enum.TryParse<RenderFidelityKind>(text.Trim(), out var result13))
			{
				return false;
			}
			o.RenderFidelity = result13;
			return true;
		}
		case "Volume":
		{
			if (!TryFloat(text, out var f22))
			{
				return false;
			}
			o.SoundVolume = Math.Clamp(f22, 0f, 1f);
			return true;
		}
		case "Looped":
		{
			if (bool.TryParse(text.Trim(), out var result10))
			{
				o.SoundLooped = result10;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.SoundLooped = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.SoundLooped = false;
				return true;
			}
			return false;
		}
		case "Playing":
		{
			if (bool.TryParse(text.Trim(), out var result6))
			{
				SetSoundPlaying(o, result6);
				return true;
			}
			if (text.Trim() == "1")
			{
				SetSoundPlaying(o, playing: true);
				return true;
			}
			if (text.Trim() == "0")
			{
				SetSoundPlaying(o, playing: false);
				return true;
			}
			return false;
		}
		case "Play on touch":
		{
			if (bool.TryParse(text.Trim(), out var result2))
			{
				o.SoundPlayOnTouch = result2;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.SoundPlayOnTouch = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.SoundPlayOnTouch = false;
				return true;
			}
			return false;
		}
		case "Hidden time":
		{
			if (!TryFloat(text, out var f29))
			{
				return false;
			}
			o.TimedHidden = Math.Clamp(f29, 0f, 3600f);
			return true;
		}
		case "Timed offset":
		{
			if (!TryFloat(text, out var f27))
			{
				return false;
			}
			o.TimedOffset = Math.Clamp(f27, -3600f, 3600f);
			return true;
		}
		case "Visible time":
		{
			if (!TryFloat(text, out var f23))
			{
				return false;
			}
			o.TimedVisible = Math.Clamp(f23, 0f, 3600f);
			return true;
		}
		case "Bounce power":
		{
			if (!TryFloat(text, out var f20))
			{
				return false;
			}
			o.BouncePower = Math.Clamp(f20, 0f, 50f);
			return true;
		}
		case "Magnet power":
		{
			if (!TryFloat(text, out var f16))
			{
				return false;
			}
			o.MagnetPower = Math.Clamp(f16, 0f, 100f);
			return true;
		}
		case "Magnet range":
		{
			if (!TryFloat(text, out var f15))
			{
				return false;
			}
			o.MagnetRange = Math.Clamp(f15, 1f, 100f);
			return true;
		}
		case "Direction":
		{
			if (!TryV3(text, out var v2))
			{
				return false;
			}
			o.VelocityDirection = v2;
			if (v2.LengthSquared > 1E-08f && o.VelocitySpeed <= 0.01f)
			{
				o.VelocitySpeed = 5f;
			}
			return true;
		}
		case "Speed":
		{
			if (!TryFloat(text, out var f8))
			{
				return false;
			}
			o.VelocitySpeed = Math.Clamp(f8, 0f, 1E+14f);
			return true;
		}
		case "Speeding Mode":
		{
			if (text.Trim() == "Accelerating")
			{
				o.VelocityMode = SpeedMode.Accelerating;
				return true;
			}
			bool flag;
			switch (text.Trim())
			{
			case "Always/Stable":
			case "Stable":
			case "Always":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				o.VelocityMode = SpeedMode.Stable;
				return true;
			}
			return false;
		}
		case "Conveyor direction":
		{
			if (!TryV3(text, out var v))
			{
				return false;
			}
			o.ConveyorDirection = v;
			return true;
		}
		case "Conveyor speed":
		{
			if (!TryFloat(text, out var f))
			{
				return false;
			}
			o.ConveyorSpeed = Math.Clamp(f, 0f, 100f);
			return true;
		}
		case "Move distance":
		{
			if (!TryFloat(text, out var f30))
			{
				return false;
			}
			o.MoveDistance = Math.Clamp(f30, 0f, 200f);
			return true;
		}
		case "Move speed":
		{
			if (!TryFloat(text, out var f28))
			{
				return false;
			}
			o.MoveSpeed = Math.Clamp(f28, 0f, 50f);
			return true;
		}
		case "Spin speed":
		{
			if (!TryFloat(text, out var f26))
			{
				return false;
			}
			o.SpinSpeed = Math.Clamp(f26, -720f, 720f);
			return true;
		}
		case "Hits to break":
		{
			if (!TryFloat(text, out var f24))
			{
				return false;
			}
			o.BreakHits = Math.Clamp((int)Math.Round(f24), 1, 99);
			return true;
		}
		case "NPC Type":
		{
			if (!Enum.TryParse<NpcKind>(text, out var result11))
			{
				return false;
			}
			o.NpcType = result11;
			return true;
		}
		case "NPC Damage":
		{
			if (!TryFloat(text, out var f19))
			{
				return false;
			}
			o.NpcDamage = Math.Clamp(f19, 0f, 100f);
			return true;
		}
		case "NPC Speed":
		{
			if (!TryFloat(text, out var f17))
			{
				return false;
			}
			o.NpcSpeed = Math.Clamp(f17, 0.5f, 12f);
			return true;
		}
		case "Dialogue":
			text = text.Trim();
			if (text.Length == 0 || text.Length > 800)
			{
				return false;
			}
			o.NpcDialogue = text;
			return true;
		case "Chat Range":
		{
			if (!TryFloat(text, out var f13))
			{
				return false;
			}
			o.NpcChatRange = Math.Clamp(f13, 4f, 40f);
			return true;
		}
		case "Chat Pause":
		{
			if (!TryFloat(text, out var f11))
			{
				return false;
			}
			o.NpcChatInterval = Math.Clamp(f11, 2f, 20f);
			return true;
		}
		case "Image Transparency":
		{
			if (!TryFloat(text, out var f10))
			{
				return false;
			}
			o.DecalTransparency = Math.Clamp(f10, 0f, 1f);
			return true;
		}
		case "Image Blur":
		{
			if (!TryFloat(text, out var f9))
			{
				return false;
			}
			o.DecalBlur = Math.Clamp(f9, 0f, 1f);
			return true;
		}
		case "Material":
		{
			if (!Enum.TryParse<MaterialKind>(text, out var result7))
			{
				return false;
			}
			o.Material = result7;
			return true;
		}
		case "Transparency":
		{
			if (!TryFloat(text, out var f6))
			{
				return false;
			}
			o.Transparency = Math.Clamp(f6, 0f, 1f);
			return true;
		}
		case "Reflection":
		case "Reflectance":
		{
			if (!TryFloat(text, out var f4))
			{
				return false;
			}
			o.Reflectance = Math.Clamp(f4, 0f, 1f);
			return true;
		}
		case "CastShadow":
		{
			if (bool.TryParse(text.Trim(), out var result3))
			{
				o.CastShadow = result3;
				return true;
			}
			if (text.Trim() == "1")
			{
				o.CastShadow = true;
				return true;
			}
			if (text.Trim() == "0")
			{
				o.CastShadow = false;
				return true;
			}
			return false;
		}
		case "Face":
		{
			bool flag;
			switch (text.Trim())
			{
			case "Front":
			case "Back":
			case "Left":
			case "Right":
			case "Top":
			case "Bottom":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				return false;
			}
			o.DecalFace = text.Trim();
			return true;
		}
		default:
			return false;
		}
	}

	private void SyncNodeName(SceneObject o)
	{
		foreach (SceneNode child in _workspace.Children)
		{
			if (child.Tag == o)
			{
				child.Name = o.Name;
				break;
			}
		}
		SelectedLabel.Text = SelectionTitle();
		StatusText.Text = "Renamed: " + o.Name;
	}

	private static string PartLabel(SceneObject o)
	{
		if (o.Shape == ShapeKind.None && o.Light != LightKind.None)
		{
			return $"{o.Name} [{o.Light}Light]";
		}
		return $"{o.Name} [{o.Shape} \u00B7 {o.Material}{(o.IsSpawn ? " \u00B7 Spawn" : "")}{(o.IsKillbrick ? $" \u00B7 Kill {o.Damage:0}" : "")}{(o.IsTeleportPad ? (" \u00B7 ?? " + o.TeleportLink) : "")}]";
	}

	private void SyncNodeIcon(SceneObject o)
	{
		foreach (SceneNode child in _workspace.Children)
		{
			if (child.Tag == o)
			{
				(string Icon, string IconPath) tuple = PartIcon(o);
				string item = tuple.Icon;
				string item2 = tuple.IconPath;
				child.Icon = item;
				child.IconPath = item2;
				if (o.Shape == ShapeKind.None && o.Light != LightKind.None)
				{
					child.Type = $"{o.Light}Light";
				}
				break;
			}
		}
	}

	private static (string Icon, string IconPath) PartIcon(SceneObject o)
	{
		if (o.Shape == ShapeKind.None && o.Light != LightKind.None)
		{
			return (Icon: "??", IconPath: "Icons/light.png");
		}
		if (o.IsSpawn)
		{
			return (Icon: "??", IconPath: "Icons/capsuleSpawn.png");
		}
		if (o.Shape == ShapeKind.Mesh)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.Shape == ShapeKind.Killbrick || o.IsKillbrick)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsTeleportPad)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsTimedPart)
		{
			return (Icon: "?", IconPath: "Icons/object.png");
		}
		if (o.IsCheckpoint)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsBouncePad)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsMagnet)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsConveyor)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsMovingPlatform)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsSpinner)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		if (o.IsBreakable)
		{
			return (Icon: "??", IconPath: "Icons/object.png");
		}
		_ = o.IsWater;
		return (Icon: "??", IconPath: "Icons/object.png");
	}

	private static bool TryFloat(string s, out float f)
	{
		return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
	}

	private static bool TryV3(string text, out OpenTK.Mathematics.Vector3 v)
	{
		v = OpenTK.Mathematics.Vector3.Zero;
		string[] array = text.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 3)
		{
			return false;
		}
		float[] array2 = new float[3];
		for (int i = 0; i < 3; i++)
		{
			if (!TryFloat(array[i], out array2[i]))
			{
				return false;
			}
		}
		v = new OpenTK.Mathematics.Vector3(array2[0], array2[1], array2[2]);
		return true;
	}

	private static List<PropertyRow> ObjectProperties(SceneObject o)
	{
		return new List<PropertyRow>
		{
			new PropertyRow
			{
				Category = "General",
				Name = "Name",
				Value = o.Name,
				Description = "Object name (must not be empty). Shows in Explorer."
			},
			new PropertyRow
			{
				Category = "General",
				Name = "Class",
				Value = "Part",
				Type = "readonly",
				Description = "Built-in class. Parts are the only editable class."
			},
			new PropertyRow
			{
				Category = "General",
				Name = "Shape",
				Type = "enum",
				Value = o.Shape.ToString(),
				Choices = Enum.GetNames<ShapeKind>(),
				Description = "Part mesh. Changes geometry and Explorer icon."
			},
			new PropertyRow
			{
				Category = "Transform",
				Name = "Position",
				Type = "vector",
				Value = PosValue(o),
				Description = "Center in studs. Format: x, y, z  (e.g. 0, 1, 0)"
			},
			new PropertyRow
			{
				Category = "Transform",
				Name = "Size",
				Type = "vector",
				Value = SizeValue(o),
				Description = "Size in studs, min 0.01 each. Format: x, y, z"
			},
			new PropertyRow
			{
				Category = "Transform",
				Name = "Rotation",
				Type = "vector",
				Value = RotValue(o),
				Description = "XYZ euler degrees. Format: x, y, z\u2026  (e.g. 0, 45, 0\u2026)"
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Color",
				Type = "color",
				Value = ColorValue(o),
				Swatch = ColorBrush(o.Color),
				Description = "Part color. RGB 0-255 (e.g. 38, 128, 242) or #hex. Click the swatch for the picker."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Material",
				Type = "enum",
				Value = o.Material.ToString(),
				Choices = Enum.GetNames<MaterialKind>(),
				Description = "Surface preset: gloss, metalness, transparency, glow."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Transparency",
				Type = "number",
				Value = o.Transparency.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Extra transparency 0-1 on top of the material. 1 = invisible."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = ((o.Shape == ShapeKind.Mesh) ? "Reflection" : "Reflectance"),
				Type = "number",
				Value = o.Reflectance.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Mirror-like sky/sun reflection 0-1 (faked, no env map)."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "CastShadow",
				Type = "bool",
				Value = o.CastShadow.ToString(),
				Description = "Off = invisible to the sun shadow map (still receives shadows)."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "Anchored",
				Type = "bool",
				Value = o.Anchored.ToString(),
				Description = "Anchored parts don't fall in Play mode (static bodies)."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "Locked",
				Type = "bool",
				Value = o.Locked.ToString(),
				Description = "Locked parts can't be picked in the viewport, dragged, or deleted. Select via Explorer."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "Spawn",
				Type = "bool",
				Value = o.IsSpawn.ToString(),
				Description = "Spawn point: the avatar starts here on Play (first one wins)."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "Mass",
				Value = o.Mass.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "How heavy the part is in Play mode, 0.01-100 (1 = default). All masses fall the same."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "TeleportLink",
				Value = o.TeleportLink,
				Description = "Teleport-pad link id (e.g. Pad1). Pads sharing an id teleport to each other. Empty = not a pad."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "CanCollide",
				Type = "bool",
				Value = o.CanCollide.ToString(),
				Description = "Off = ghost: renders and selects, but falls through everything."
			}
		}.Concat(o.HasVelocityConfig ? VelocityProperties(o) : Enumerable.Empty<PropertyRow>()).Concat(o.IsTimedPart ? TimedProperties(o) : Enumerable.Empty<PropertyRow>()).Concat(o.IsBouncePad ? BounceProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(o.IsMagnet ? MagnetProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(o.IsConveyor ? ConveyorProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(o.IsMovingPlatform ? MovingProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(o.IsSpinner ? SpinnerProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(o.IsBreakable ? BreakableProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(SoundProperties(o))
			.Concat((o.Shape == ShapeKind.Mesh) ? MeshProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(o.IsNpc ? NpcProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(DecalProperties(o))
			.Concat(TextProperties(o))
			.Concat(TextureProperties(o))
			.Concat((o.Shape == ShapeKind.Killbrick || o.Damage > 0.01f) ? DamageProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat((MaterialParams.Of(o.Material).Texture != null) ? TilingProperties(o) : Enumerable.Empty<PropertyRow>())
			.Concat(EmitterProperties(o))
			.Concat(ScriptProperties(o))
			.ToList();
	}

	private static IEnumerable<PropertyRow> ScriptProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Script",
			Name = "Edit script",
			Type = "action",
			Value = (string.IsNullOrWhiteSpace(o.Script) ? "Empty" : FirstScriptLine(o.Script)),
			Description = "Lua run in Play mode: onTick(dt), onTouch(player). Empty = disabled."
		};
	}

	private static string FirstScriptLine(string s)
	{
		string text = (s ?? "").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
		if (text.Length <= 40)
		{
			return text;
		}
		return text.Substring(0, 40);
	}

	private static IEnumerable<PropertyRow> MagnetProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Magnet",
			Name = "Magnet power",
			Value = o.MagnetPower.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Pull strength in studs/s\u00B2 (default 15). Pulls unanchored parts and the avatar."
		};
		yield return new PropertyRow
		{
			Category = "Magnet",
			Name = "Magnet range",
			Value = o.MagnetRange.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Reach in studs (default 10)."
		};
	}

	private static IEnumerable<PropertyRow> ConveyorProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Conveyor",
			Name = "Conveyor direction",
			Type = "vector",
			Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", o.ConveyorDirection.X, o.ConveyorDirection.Y, o.ConveyorDirection.Z),
			Description = "Part-relative belt direction (Y is flattened). Rotating the part steers it."
		};
		yield return new PropertyRow
		{
			Category = "Conveyor",
			Name = "Conveyor speed",
			Value = o.ConveyorSpeed.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Belt speed in studs/s (default 8). Riders on top move at this speed."
		};
	}

	private static IEnumerable<PropertyRow> MovingProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Moving",
			Name = "Move distance",
			Value = o.MoveDistance.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Travel in studs each way along part-local X, no waypoints (default 8). Must stay anchored."
		};
		yield return new PropertyRow
		{
			Category = "Moving",
			Name = "Move speed",
			Value = o.MoveSpeed.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Travel speed in studs/s (default 2). Loops forever in Play mode."
		};
		yield return new PropertyRow
		{
			Category = "Moving",
			Name = "Add waypoint",
			Type = "action",
			Value = "Add waypoint",
			Description = "Capture the platform's current spot as a new loop stop (click a ball to select it)."
		};
		yield return new PropertyRow
		{
			Category = "Moving",
			Name = "Remove waypoint",
			Type = "action",
			Value = "Remove waypoint",
			Description = "Delete the selected waypoint ball."
		};
		yield return new PropertyRow
		{
			Category = "Moving",
			Name = "Waypoints",
			Type = "action",
			Value = ((o.Waypoints.Count == 0) ? "Waypoint list\u2026" : $"{o.Waypoints.Count} point{((o.Waypoints.Count == 1) ? "" : "s")}\u2026"),
			Description = "Open the waypoint list (stays open while you move the platform and capture stops)."
		};
	}

	private static IEnumerable<PropertyRow> SpinnerProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Spinner",
			Name = "Spin speed",
			Value = o.SpinSpeed.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Rotation in degrees/s around Y (default 45, negative reverses). Must stay anchored."
		};
	}

	private static IEnumerable<PropertyRow> BreakableProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Breakable",
			Name = "Hits to break",
			Value = o.BreakHits.ToString(CultureInfo.InvariantCulture),
			Description = "Stomp landings needed to shatter it (default 2: first thuds, second breaks)."
		};
	}

	private static IEnumerable<PropertyRow> MeshProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Mesh",
			Name = "Mesh file",
			Type = "file",
			Value = (o.MeshPath ?? ""),
			Description = "Imported model (fbx, obj). Click to replace. Renders the file, collides as a box."
		};
		yield return new PropertyRow
		{
			Category = "Mesh",
			Name = "Collision fidelity",
			Type = "enum",
			Value = o.CollisionFidelity.ToString(),
			Choices = Enum.GetNames<CollisionFidelityKind>(),
			Description = "Box = bounding-box collision. Precise = exact file triangles when anchored, convex hull when unanchored."
		};
		yield return new PropertyRow
		{
			Category = "Mesh",
			Name = "Render fidelity",
			Type = "enum",
			Value = o.RenderFidelity.ToString(),
			Choices = Enum.GetNames<RenderFidelityKind>(),
			Description = "Performance = decimated lowpoly. Normal = file as imported. Ultra = full detail, resmoothed shading."
		};
		yield return new PropertyRow
		{
			Category = "Mesh",
			Name = "Color Brightness",
			Type = "number",
			Value = o.ColorBrightness.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Color multiplier 0-50 (default 1). Brightens or darkens the mesh without touching its part color."
		};
	}

	private static IEnumerable<PropertyRow> SoundProperties(SceneObject o)
	{
		if (!o.HasSound)
		{
			yield return new PropertyRow
			{
				Category = "Sound",
				Name = "Add sound",
				Type = "action",
				Value = "Add sound\u2026",
				Description = "Attach an audio file to this part (wav, mp3, aiff). Plays positionally."
			};
			yield break;
		}
		yield return new PropertyRow
		{
			Category = "Sound",
			Name = "Sound",
			Type = "file",
			Value = (o.SoundPath ?? ""),
			Description = "Audio file (wav, mp3, aiff). Click to replace. Bundled into Assets/ on publish."
		};
		yield return new PropertyRow
		{
			Category = "Sound",
			Name = "Volume",
			Type = "number",
			Value = o.SoundVolume.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Loudness 0-1 (default 0.5). Distance also fades it, Roblox-style."
		};
		yield return new PropertyRow
		{
			Category = "Sound",
			Name = "Looped",
			Type = "bool",
			Value = o.SoundLooped.ToString(),
			Description = "On = repeats instead of playing once."
		};
		yield return new PropertyRow
		{
			Category = "Sound",
			Name = "Playing",
			Type = "bool",
			Value = o.SoundPlaying.ToString(),
			Description = "On = plays in Play mode (positionally). Edit mode stays silent."
		};
		yield return new PropertyRow
		{
			Category = "Sound",
			Name = "Play on touch",
			Type = "bool",
			Value = o.SoundPlayOnTouch.ToString(),
			Description = "On = restarts the sound from the top when the avatar touches the part (1s cooldown)."
		};
		yield return new PropertyRow
		{
			Category = "Sound",
			Name = "Remove sound",
			Type = "action",
			Value = "Remove sound",
			Description = "Detach the sound from this part (stops playback)."
		};
	}

	private static IEnumerable<PropertyRow> TimedProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Timed",
			Name = "Hidden time",
			Value = o.TimedHidden.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Hidden phase length in seconds (default 1). Invisible and non-collidable while hidden."
		};
		yield return new PropertyRow
		{
			Category = "Timed",
			Name = "Timed offset",
			Value = o.TimedOffset.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Phase shift in seconds into the cycle (default 0 = starts visible)."
		};
		yield return new PropertyRow
		{
			Category = "Timed",
			Name = "Visible time",
			Value = o.TimedVisible.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Visible phase length in seconds (default 1). Solid and rendered while visible."
		};
	}

	private static IEnumerable<PropertyRow> DamageProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Behavior",
			Name = "Damage",
			Value = o.Damage.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Killbrick damage 0-1000. 0 = harmless, 100 = one-shot kill. Only the Killbrick type uses it."
		};
	}

	private static IEnumerable<PropertyRow> TilingProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Appearance",
			Name = "Tiling",
			Value = o.Tiling.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Texture tiles per stud for this material, 0.1-8 (default 1 = one texture per stud)."
		};
	}

	private static IEnumerable<PropertyRow> EmitterProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Particles",
			Name = "Effect",
			Type = "enum",
			Value = o.ParticlePreset,
			Choices = new string[5] { "None", "Custom", "Fire", "Smoke", "Sparkles" },
			Description = "Particle effect. None = off; named presets overwrite the values below."
		};
		if (!(o.ParticlePreset == "None"))
		{
			yield return new PropertyRow
			{
				Category = "Particles",
				Name = "Rate",
				Value = o.EmissionRate.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Spawn rate in particles/sec, 0-200 (default 20)."
			};
			yield return new PropertyRow
			{
				Category = "Particles",
				Name = "Lifetime",
				Value = o.ParticleLifetime.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Particle lifetime in seconds, 0.1-10 (default 1.5)."
			};
			yield return new PropertyRow
			{
				Category = "Particles",
				Name = "Particle speed",
				Value = o.ParticleSpeed.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Launch speed in studs/s, 0-50 (default 5)."
			};
			yield return new PropertyRow
			{
				Category = "Particles",
				Name = "Particle size",
				Value = o.ParticleSize.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Sprite diameter in studs, 0.1-4 (default 0.5). Color comes from the part color."
			};
			yield return new PropertyRow
			{
				Category = "Particles",
				Name = "Spread",
				Value = o.ParticleSpread.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Cone spread 0-1: 0 = straight up, 1 = all directions (default 0.3)."
			};
			yield return new PropertyRow
			{
				Category = "Particles",
				Name = "Gravity",
				Value = o.ParticleGravity.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Downward pull in studs/s\u00B2, -20-20 (default 0; negative floats up)."
			};
		}
	}

	private static void ApplyParticlePreset(SceneObject o, string preset)
	{
		switch (preset)
		{
		case "Fire":
			o.Color = new Color4(1f, 0.45f, 0.1f, 1f);
			o.EmissionRate = 120f;
			o.ParticleLifetime = 0.75f;
			o.ParticleSpeed = 4.5f;
			o.ParticleSize = 0.5f;
			o.ParticleSpread = 0.22f;
			o.ParticleGravity = -6f;
			break;
		case "Smoke":
			o.Color = new Color4(0.55f, 0.55f, 0.58f, 1f);
			o.EmissionRate = 25f;
			o.ParticleLifetime = 3f;
			o.ParticleSpeed = 1.5f;
			o.ParticleSize = 1f;
			o.ParticleSpread = 0.35f;
			o.ParticleGravity = -1.2f;
			break;
		case "Sparkles":
			o.Color = new Color4(1f, 0.95f, 0.6f, 1f);
			o.EmissionRate = 40f;
			o.ParticleLifetime = 1.2f;
			o.ParticleSpeed = 3f;
			o.ParticleSize = 0.3f;
			o.ParticleSpread = 1f;
			o.ParticleGravity = 1f;
			break;
		}
	}

	private static IEnumerable<PropertyRow> BounceProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Bounce",
			Name = "Bounce power",
			Value = o.BouncePower.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Launch velocity in studs/s (default 20). Jump power is ~9 for scale."
		};
	}

	private static IEnumerable<PropertyRow> VelocityProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "Velocity",
			Name = "Direction",
			Type = "vector",
			Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", o.VelocityDirection.X, o.VelocityDirection.Y, o.VelocityDirection.Z),
			Description = "Part-relative travel direction (x, y, z): rotating the part steers the thrust. Zero = off. Normalized automatically. Accelerating cruises the flat plane (vertical travel needs Always/Stable)."
		};
		yield return new PropertyRow
		{
			Category = "Velocity",
			Name = "Speed",
			Value = o.VelocitySpeed.ToString("0.00", CultureInfo.InvariantCulture),
			Description = "Travel speed 0-100 studs/s. 0 = off. Needs an unanchored part."
		};
		yield return new PropertyRow
		{
			Category = "Velocity",
			Name = "Speeding Mode",
			Type = "enum",
			Value = ((o.VelocityMode == SpeedMode.Accelerating) ? "Accelerating" : "Always/Stable"),
			Choices = new string[2] { "Accelerating", "Always/Stable" },
			Description = "Accelerating ramps up to Speed (gravity still pulls); Always/Stable holds the exact Speed with no ramp or braking. Both cruise the flat plane \u2014 gravity always pulls."
		};
		yield return new PropertyRow
		{
			Category = "Velocity",
			Name = "Remove velocity",
			Type = "action",
			Value = "Remove velocity",
			Description = "Remove velocity from this part (clears direction, speed and mode)."
		};
	}

	private static IEnumerable<PropertyRow> NpcProperties(SceneObject o)
	{
		yield return new PropertyRow
		{
			Category = "NPC",
			Name = "NPC Type",
			Type = "enum",
			Value = o.NpcType.ToString(),
			Choices = Enum.GetNames<NpcKind>(),
			Description = "Enemy chases and taunts. Friendly stays put and greets the player."
		};
		if (o.NpcType == NpcKind.Enemy)
		{
			yield return new PropertyRow
			{
				Category = "NPC",
				Name = "NPC Damage",
				Value = o.NpcDamage.ToString("0.0", CultureInfo.InvariantCulture),
				Description = "Damage per touch tick, 0-100."
			};
			yield return new PropertyRow
			{
				Category = "NPC",
				Name = "NPC Speed",
				Value = o.NpcSpeed.ToString("0.0", CultureInfo.InvariantCulture),
				Description = "Chase speed in studs per second, 0.5-12."
			};
			yield return new PropertyRow
			{
				Category = "NPC",
				Name = "Dialogue",
				Type = "action",
				Value = "Edit taunts\u2026",
				Description = "Enemy taunts (| separated). A bubble pops overhead when the player is in Chat Range."
			};
		}
		else
		{
			yield return new PropertyRow
			{
				Category = "NPC",
				Name = "Dialogue",
				Type = "action",
				Value = "Edit dialogue\u2026",
				Description = "Friendly lines (| separated). Greets on approach, then chats every Chat Pause."
			};
		}
		yield return new PropertyRow
		{
			Category = "NPC",
			Name = "Chat Range",
			Value = o.NpcChatRange.ToString("0.0", CultureInfo.InvariantCulture),
			Description = "How close the player must be (studs) for this NPC to chat, 4-40."
		};
		yield return new PropertyRow
		{
			Category = "NPC",
			Name = "Chat Pause",
			Value = o.NpcChatInterval.ToString("0.0", CultureInfo.InvariantCulture),
			Description = "Pause between chat lines in seconds, 2-20."
		};
		yield return new PropertyRow
		{
			Category = "NPC",
			Name = "Face",
			Type = "file",
			Value = (o.NpcFaceImage ?? "None"),
			Thumbnail = LoadThumbnail(o.NpcFaceImage),
			Description = "NPC face image (empty = stock face). Click \u2026 to choose."
		};
	}

	private static IReadOnlyList<string> FontNames()
	{
		return _fontNames ?? (_fontNames = System.Drawing.FontFamily.Families.Select((System.Drawing.FontFamily f) => f.Name).OrderBy<string, string>((string n) => n, StringComparer.OrdinalIgnoreCase).ToList());
	}

	private static IEnumerable<PropertyRow> TextProperties(SceneObject o)
	{
		for (int i = 0; i < o.Texts.Count; i++)
		{
			TextLayer t = o.Texts[i];
			string cat = $"Text {i + 1}";
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Text",
				Value = t.Text,
				TextIndex = i,
				Description = $"Text {i + 1} content (up to 200 characters)."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Face",
				Type = "enum",
				Value = t.Face,
				TextIndex = i,
				Choices = new string[6] { "Front", "Back", "Left", "Right", "Top", "Bottom" },
				Description = $"Which part face shows text {i + 1}."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Font",
				Type = "enum",
				Value = t.Font,
				TextIndex = i,
				Choices = FontNames(),
				Description = $"System font for text {i + 1}."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Size",
				Value = t.Size.ToString("0", CultureInfo.InvariantCulture),
				TextIndex = i,
				Description = $"Texture pixels 8-256 for text {i + 1} (per-face resolution)."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Color",
				Type = "color",
				Value = t.Color,
				TextIndex = i,
				Swatch = TextSwatch(t.Color),
				Description = $"Text {i + 1} color. RGB 0-255 (e.g. 255, 255, 255) or #hex."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Text Transparency",
				Type = "number",
				Value = t.Transparency.ToString("0.00", CultureInfo.InvariantCulture),
				TextIndex = i,
				Description = "0 = opaque, 1 = invisible. Drag the slider or type 0-1."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Text Blur",
				Type = "number",
				Value = t.Blur.ToString("0.00", CultureInfo.InvariantCulture),
				TextIndex = i,
				Description = "Softens the text. 0 = sharp, 1 = max blur."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Bold",
				Type = "bool",
				Value = t.Bold.ToString(),
				TextIndex = i,
				Description = "Use the bold face of the selected font when available."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Outline",
				Type = "number",
				Value = t.Outline.ToString("0.0", CultureInfo.InvariantCulture),
				TextIndex = i,
				Description = "Outline thickness in pixels, 0-16. Helps text remain readable at a distance."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Outline Color",
				Type = "color",
				Value = t.OutlineColor,
				TextIndex = i,
				Swatch = TextSwatch(t.OutlineColor),
				Description = "Outline RGB 0-255 or #hex. Black is best for bright signs."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Offset",
				Value = OffsetValue(t.OffsetX, t.OffsetY),
				TextIndex = i,
				Description = $"Text {i + 1} center on the face, x right / y up (-1 to 1). 0, 0 = centered."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Scale",
				Type = "number",
				Value = t.Scale.ToString("0.00", CultureInfo.InvariantCulture),
				TextIndex = i,
				Description = $"Fraction of the face text {i + 1} covers. 1 = full face."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Remove",
				Type = "action",
				Value = "Remove",
				TextIndex = i,
				Description = $"Remove text {i + 1} from this part."
			};
		}
		if (o.Texts.Count < 6)
		{
			yield return new PropertyRow
			{
				Category = "Text",
				Name = "Add text",
				Type = "action",
				Value = ((o.Texts.Count == 0) ? "+ Add text" : "+ Add another"),
				Description = $"Add rendered text (up to {6} per part, any face)."
			};
		}
	}

	private static System.Windows.Media.Brush TextSwatch(string color)
	{
		if (!PartColor.TryParseRgb(color, out var color2) && !PartColor.TryParseHex(color, out color2))
		{
			color2 = System.Windows.Media.Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue);
		}
		SolidColorBrush solidColorBrush = new SolidColorBrush(color2);
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static IEnumerable<PropertyRow> DecalProperties(SceneObject o)
	{
		if (o.Decals.Count == 0 && !string.IsNullOrWhiteSpace(o.DecalImage))
		{
			yield return new PropertyRow
			{
				Category = "Decal",
				Name = "Image",
				Type = "file",
				Value = o.DecalImage,
				Thumbnail = LoadThumbnail(o.DecalImage),
				Description = "Decal image file. Click \u2026 to replace."
			};
			yield return new PropertyRow
			{
				Category = "Decal",
				Name = "Face",
				Type = "enum",
				Value = o.DecalFace,
				Choices = new string[6] { "Front", "Back", "Left", "Right", "Top", "Bottom" },
				Description = "Which part face shows the decal."
			};
			yield return new PropertyRow
			{
				Category = "Decal",
				Name = "Image Transparency",
				Type = "number",
				Value = o.DecalTransparency.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "0 = opaque, 1 = invisible. Drag the slider or type 0-1."
			};
			yield return new PropertyRow
			{
				Category = "Decal",
				Name = "Image Blur",
				Type = "number",
				Value = o.DecalBlur.ToString("0.00", CultureInfo.InvariantCulture),
				Description = "Softens the decal (9-tap Gaussian). 0 = sharp, 1 = max blur."
			};
			yield return new PropertyRow
			{
				Category = "Decal",
				Name = "Remove",
				Type = "action",
				Value = "Remove",
				Description = "Remove the decal from this part."
			};
		}
		else
		{
			for (int i = 0; i < o.Decals.Count; i++)
			{
				DecalLayer d = o.Decals[i];
				string cat = $"Decal {i + 1}";
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Image",
					Type = "file",
					Value = d.Image,
					DecalIndex = i,
					Thumbnail = LoadThumbnail(d.Image),
					Description = $"Decal {i + 1} image file. Click \u2026 to replace."
				};
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Face",
					Type = "enum",
					Value = d.Face,
					DecalIndex = i,
					Choices = new string[6] { "Front", "Back", "Left", "Right", "Top", "Bottom" },
					Description = $"Which part face shows decal {i + 1}."
				};
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Image Transparency",
					Type = "number",
					Value = d.Transparency.ToString("0.00", CultureInfo.InvariantCulture),
					DecalIndex = i,
					Description = "0 = opaque, 1 = invisible. Drag the slider or type 0-1."
				};
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Image Blur",
					Type = "number",
					Value = d.Blur.ToString("0.00", CultureInfo.InvariantCulture),
					DecalIndex = i,
					Description = "Softens the decal (9-tap Gaussian). 0 = sharp, 1 = max blur."
				};
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Offset",
					Value = OffsetValue(d.OffsetX, d.OffsetY),
					DecalIndex = i,
					Description = $"Decal {i + 1} center on the face, x right / y up (-1 to 1). 0, 0 = centered."
				};
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Scale",
					Type = "number",
					Value = d.Scale.ToString("0.00", CultureInfo.InvariantCulture),
					DecalIndex = i,
					Description = $"Fraction of the face decal {i + 1} covers. 1 = full face."
				};
				yield return new PropertyRow
				{
					Category = cat,
					Name = "Remove",
					Type = "action",
					Value = "Remove",
					DecalIndex = i,
					Description = $"Remove decal {i + 1} from this part."
				};
			}
		}
		if (o.Decals.Count < 6)
		{
			yield return new PropertyRow
			{
				Category = "Decal",
				Name = "Add decal",
				Type = "action",
				Value = ((o.Decals.Count == 0 && string.IsNullOrWhiteSpace(o.DecalImage)) ? "+ Add decal" : "+ Add another"),
				Description = $"Add an image decal (up to {6} per part, any face)."
			};
		}
	}

	private static IEnumerable<PropertyRow> TextureProperties(SceneObject o)
	{
		for (int i = 0; i < o.Textures.Count; i++)
		{
			TextureLayer t = o.Textures[i];
			string cat = $"Texture {i + 1}";
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Texture",
				Type = "file",
				Value = t.Image,
				TextureIndex = i,
				Thumbnail = LoadThumbnail(t.Image),
				Description = $"Texture {i + 1} image file. Click \u2026 to replace."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Texture Face",
				Type = "enum",
				Value = t.Face,
				TextureIndex = i,
				Choices = new string[6] { "Front", "Back", "Left", "Right", "Top", "Bottom" },
				Description = $"Which part face texture {i + 1} tiles across."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Texture Transparency",
				Type = "number",
				Value = t.Transparency.ToString("0.00", CultureInfo.InvariantCulture),
				TextureIndex = i,
				Description = "0 = opaque, 1 = invisible. Drag the slider or type 0-1."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Tiling X",
				Type = "number",
				Value = t.TilingX.ToString("0.00", CultureInfo.InvariantCulture),
				TextureIndex = i,
				Description = "Tiles across the face per stud, 0.1-32 (1 = one tile per stud, like materials)."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Tiling Y",
				Type = "number",
				Value = t.TilingY.ToString("0.00", CultureInfo.InvariantCulture),
				TextureIndex = i,
				Description = "Tiles up the face per stud, 0.1-32 (1 = one tile per stud, like materials)."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Tiling Offset X",
				Type = "number",
				Value = t.OffsetX.ToString("0.00", CultureInfo.InvariantCulture),
				TextureIndex = i,
				Description = "Horizontal UV scroll, wraps (default 0)."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Tiling Offset Y",
				Type = "number",
				Value = t.OffsetY.ToString("0.00", CultureInfo.InvariantCulture),
				TextureIndex = i,
				Description = "Vertical UV scroll, wraps (default 0)."
			};
			yield return new PropertyRow
			{
				Category = cat,
				Name = "Remove",
				Type = "action",
				Value = "Remove",
				TextureIndex = i,
				Description = $"Remove texture {i + 1} from this part."
			};
		}
		if (o.Textures.Count < 4)
		{
			yield return new PropertyRow
			{
				Category = "Texture",
				Name = "Add texture",
				Type = "action",
				Value = ((o.Textures.Count == 0) ? "+ Add texture" : "+ Add another"),
				Description = $"Add a tiling image texture (up to {4} per part, any face)."
			};
		}
	}

	private static string OffsetValue(float x, float y)
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:0.00}, {1:0.00}", x, y);
	}

	private static bool TryV2(string text, out float x, out float y)
	{
		x = (y = 0f);
		string[] array = text.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 2)
		{
			return false;
		}
		if (TryFloat(array[0], out x))
		{
			return TryFloat(array[1], out y);
		}
		return false;
	}

	private static string PosValue(SceneObject o)
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", o.Position.X, o.Position.Y, o.Position.Z);
	}

	private static string SizeValue(SceneObject o)
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", o.Size.X, o.Size.Y, o.Size.Z);
	}

	private static string RotValue(SceneObject o)
	{
		return string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0}, {2:0}\u00B0", o.Rotation.X, o.Rotation.Y, o.Rotation.Z);
	}

	private static string ColorValue(SceneObject o)
	{
		return $"{ToByte(o.Color.R)}, {ToByte(o.Color.G)}, {ToByte(o.Color.B)}";
	}

	private static byte ToByte(float v)
	{
		return (byte)Math.Round(Math.Clamp(v * 255f, 0f, 255f));
	}

	private static float Component(OpenTK.Mathematics.Vector3 v, int axis)
	{
		return axis switch
		{
			1 => v.Y, 
			0 => v.X, 
			_ => v.Z, 
		};
	}

	private float SnapScaleCoordinate(float coordinate, bool sym)
	{
		if (SnapCheck.IsChecked != true || _dragAxis < 0)
		{
			return coordinate;
		}
		float num = Component(_dragSize, _dragAxis);
		float num2 = (sym ? 2f : 1f);
		float v = Math.Max(num + (coordinate - _dragCoord0) * _dragSign * num2, 0.01f);
		v = MathF.Max(Math.Max(SnapIncrement(), 0.01f), SnapFloat(v));
		return _dragCoord0 + (v - num) * _dragSign / num2;
	}

	private static void SetComponent(ref OpenTK.Mathematics.Vector3 v, int axis, float value)
	{
		switch (axis)
		{
		case 0:
			v.X = value;
			break;
		case 1:
			v.Y = value;
			break;
		default:
			v.Z = value;
			break;
		}
	}

	private void ApplyScaleSize(float s, bool sym, bool uni)
	{
		if (_scene.Selected == null || _dragAxis < 0)
		{
			return;
		}
		float num = s - _dragCoord0;
		float num2 = ((sym && !uni) ? 2f : 1f);
		foreach (SceneObject item in DragTargets())
		{
			if (item.Locked || !_dragSizes.TryGetValue(item, out var value))
			{
				continue;
			}
			float num3 = Math.Max(Component(value, _dragAxis) + num * _dragSign * num2, 0.01f);
			if (SnapCheck.IsChecked == true)
			{
				num3 = MathF.Max(Math.Max(SnapIncrement(), 0.01f), SnapFloat(num3));
			}
			if (uni)
			{
				float num4 = Math.Clamp(num3 / Math.Max(Component(value, _dragAxis), 1E-06f), 0.01f, 100f);
				OpenTK.Mathematics.Vector3 size = new OpenTK.Mathematics.Vector3(Math.Max(value.X * num4, 0.01f), Math.Max(value.Y * num4, 0.01f), Math.Max(value.Z * num4, 0.01f));
				if (SnapCheck.IsChecked == true)
				{
					size = new OpenTK.Mathematics.Vector3(MathF.Max(Math.Max(SnapIncrement(), 0.01f), SnapFloat(size.X)), MathF.Max(Math.Max(SnapIncrement(), 0.01f), SnapFloat(size.Y)), MathF.Max(Math.Max(SnapIncrement(), 0.01f), SnapFloat(size.Z)));
				}
				if (!_dragBottoms.TryGetValue(item, out var value2))
				{
					value2 = BottomCenter(item);
				}
				item.Size = size;
				item.Position = value2 + OpenTK.Mathematics.Vector3.Transform(new OpenTK.Mathematics.Vector3(0f, size.Y / 2f, 0f), item.Orientation);
			}
			else
			{
				OpenTK.Mathematics.Vector3 v = item.Size;
				float num5 = Component(v, _dragAxis);
				SetComponent(ref v, _dragAxis, num3);
				item.Size = v;
				float num6 = num3 - num5;
				if (num6 != 0f && !sym)
				{
					item.Position += _scene.GizmoAxis(_dragAxis) * (_dragSign * num6 / 2f);
				}
			}
		}
		SyncGizmoPivot();
	}

	private void Search_GotFocus(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.TextBox { Text: "Search" } textBox)
		{
			textBox.Text = "";
		}
	}

	private void Search_LostFocus(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.TextBox textBox && string.IsNullOrWhiteSpace(textBox.Text))
		{
			textBox.Text = "Search";
		}
	}

	private void Log(string msg)
	{
		OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
		if (OutputBox.LineCount > 800)
		{
			OutputBox.Text = OutputBox.Text.Substring(OutputBox.GetCharacterIndexFromLineIndex(200));
		}
		OutputBox.ScrollToEnd();
	}

	private int CountObjects()
	{
		int num = 0;
		IEnumerable itemsSource = ExplorerTree.ItemsSource;
		if (itemsSource != null)
		{
			foreach (SceneNode item in itemsSource)
			{
				num += 1 + CountChildren(item);
			}
		}
		return num;
	}

	private static int CountChildren(SceneNode n)
	{
		int num = n.Children.Count;
		foreach (SceneNode child in n.Children)
		{
			num += CountChildren(child);
		}
		return num;
	}

	private static List<PropertyRow> DefaultProperties(string name, string type = "Part")
	{
		return new List<PropertyRow>
		{
			new PropertyRow
			{
				Category = "General",
				Name = "Name",
				Value = name,
				Description = "Service or container name (display only)."
			},
			new PropertyRow
			{
				Category = "General",
				Name = "Class",
				Value = type,
				Type = "readonly",
				Description = "Built-in class. Select a Part to edit live properties."
			},
			new PropertyRow
			{
				Category = "Transform",
				Name = "Position",
				Value = "0, 0, 0",
				Type = "vector",
				Description = "Select a Part to edit its transform."
			},
			new PropertyRow
			{
				Category = "Transform",
				Name = "Size",
				Value = "2, 2, 2",
				Type = "vector",
				Description = "Select a Part to edit its size."
			},
			new PropertyRow
			{
				Category = "Transform",
				Name = "Rotation",
				Value = "0, 0, 0",
				Type = "vector",
				Description = "Select a Part to edit its rotation."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Color",
				Value = "38, 128, 242",
				Type = "color",
				Swatch = new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 128, 242)),
				Description = "Select a Part to edit its color."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Material",
				Value = "Plastic",
				Type = "readonly",
				Description = "Select a Part to change its material."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Transparency",
				Value = "0.00",
				Type = "readonly",
				Description = "Select a Part to edit its transparency."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "Reflectance",
				Value = "0.00",
				Type = "readonly",
				Description = "Select a Part to edit its reflection."
			},
			new PropertyRow
			{
				Category = "Appearance",
				Name = "CastShadow",
				Value = "True",
				Type = "readonly",
				Description = "Select a Part to toggle its shadow casting."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "Anchored",
				Type = "bool",
				Value = "True",
				Description = "Display only for services. Parts use this to freeze physics."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "Mass",
				Value = "1.00",
				Type = "readonly",
				Description = "Select a Part to edit how heavy it is."
			},
			new PropertyRow
			{
				Category = "Behavior",
				Name = "CanCollide",
				Value = "True",
				Type = "readonly",
				Description = "Select a Part to toggle ghost collision."
			}
		};
	}

}

