using System.Reflection;
using System.Runtime.Loader;
using Engine.Client.Graphics;
using Engine.Shared.Objects;
using Engine.Shared.Prototypes;
using Engine.Shared.Serialization;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Engine.Client;

/// <summary>
///		Owns the engine's managers and runs the game from startup to exit.
/// </summary>
public sealed class GameController
{
	public ComponentFactory Components { get; } = new();
	public SerializationManager Serialization { get; }
	public PrototypeManager Prototypes { get; }

	/// <summary>
	///		The Resources folder. Set by Startup.
	/// </summary>
	private string _resources = "";

	private IWindow? _window;
	private IInputContext? _input;
	private IRenderer? _renderer;

	/// <summary>
	///		The do-nothing refresh callback from <see cref="DisableGlfwRefreshOnWayland"/>.
	///		Kept in a field so the garbage collector can't free it while GLFW still holds a pointer to it.
	/// </summary>
	private GlfwCallbacks.WindowRefreshCallback? _noOpRefresh;

	/// <summary>
	///		What Run returns once the window closes. Set to 1 if something fails inside a window event.
	/// </summary>
	private int _exitCode;

	public GameController()
	{
		// Each manager needs the one before it, so they're created in this order.
		Serialization = new SerializationManager(Components);
		Prototypes = new PrototypeManager(Serialization);
	}

	/// <returns>The process exit code: 0 on success.</returns>
	public int Run()
	{
		Console.WriteLine("RobustDaggerfall starting.");

		if (!Startup())
			return 1;

		foreach (var entity in Prototypes.EnumeratePrototypes<EntityPrototype>())
			Console.WriteLine($"  entity {entity.ID}: {entity.Name} [{string.Join(", ", entity.Components.Keys)}]");

		RunWindow();
		return _exitCode;
	}

	/// <summary>
	///		Opens the window and runs the game loop until it's closed.
	/// </summary>
	/// <remarks>
	///		Silk.NET runs the loop itself and calls us back through events:
	///			Load once at the start, then Update and Render every frame, then closes at the end.
	///		window.Run() doesn't return until the window closes.
	/// </remarks>
	private void RunWindow()
	{
		var options = WindowOptions.Default with
		{
			Title = "RobustDaggerfall",
			Size = new Vector2D<int>(1280, 720),
			VSync = true,

			// We ask for OpenGL 3.3's core, aka the modern subset without the old fixed-function calls.
			// ForwardCompatible is required by macOS and harmless everywhere else.
			API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
		};

		_window = Window.Create(options);
		_window.Load += OnLoad;
		_window.Render += OnRender;
		_window.FramebufferResize += OnFramebufferResize;
		_window.Closing += OnClosing;

		_window.Run();
		_window.Dispose();
	}

	private void OnLoad()
	{
		var window = _window!;

		// The OpenGL functions only exist once the window (and its OpenGL context) does, so this has to wait until Load.
		var gl = GL.GetApi(window);

		DisableGlfwRefreshOnWayland(window);

		_input = window.CreateInput();
		foreach (var keyboard in _input.Keyboards)
		{
			keyboard.KeyDown += (_, key, _) =>
			{
				if (key == Key.Escape)
					window.Close();
			};
		}

		try
		{
			_renderer = new OpenGLRenderer(gl, Path.Combine(_resources, "Shaders"));
			_renderer.Initialize();
		}
		catch (Exception e) when (e is ShaderException or IOException)
		{
			Console.Error.WriteLine($"Renderer failed to start: {e.Message}");
			_exitCode = 1;
			window.Close();
			return;
		}

		// FramebufferSize is in real pixels. On high-DPI screens it can be bigger than the window's Size.
		_renderer.Resize(window.FramebufferSize.X, window.FramebufferSize.Y);
	}

	/// <summary>
	///		A work-around for slow drag-resizing on Linux Wayland: each resize step fires GLFW's refresh callback,
	///		and Silk.NET answers it with a whole extra update, draw and buffer swap.
	///		A no-op callback stops that, and the normal loop still redraws every frame.
	/// </summary>
	private unsafe void DisableGlfwRefreshOnWayland(IWindow window)
	{
		if (!OperatingSystem.IsLinux() || window.Native?.Glfw is not { } handle)
			return;

		_noOpRefresh = _ => { };
		Glfw.GetApi().SetWindowRefreshCallback((WindowHandle*) handle, _noOpRefresh);
	}

	private void OnRender(double deltaTime) => _renderer?.Render(deltaTime);

	private void OnFramebufferResize(Vector2D<int> size)
	{
		_renderer?.Resize(size.X, size.Y);
		_renderer?.Render(0);
	}

	private void OnClosing()
	{
		// Free GPU resources while the OpenGL context still exists. After Closing, it's gone.
		_renderer?.Dispose();
		_input?.Dispose();
	}

	/// <summary>
	///		Registers types and loads resources. Prints the reason and returns false if anything fails.
	/// </summary>
	private bool Startup()
	{
		var root = FindRoot();
		if (root is null)
		{
			Console.Error.WriteLine("Could not find the Resources folder.");
			return false;
		}

		var resources = Path.Combine(root, "Resources");
		_resources = resources;
		Console.WriteLine($"Resources: {resources}");

		var content = LoadContent(root);
		if (content is null)
			return false;

		Console.WriteLine($"Content: {content.FullName}");

		// The factory must know the components before any YAML is read, so register first.
		// Engine first, then content, so content can't quietly take a name the engine already uses.
		Components.RegisterAssembly(typeof(Component).Assembly);
		Components.RegisterAssembly(content);
		Prototypes.RegisterKindsFrom(content);

		try
		{
			Prototypes.LoadDirectory(Path.Combine(resources, "Prototypes"));
		}
		catch (PrototypeLoadException e)
		{
			Console.Error.WriteLine($"Failed to load prototypes: {e.Message}");
			return false;
		}

		return true;
	}

	/// <summary>
	///		Loads the content DLL that Content.Shared builds into bin/Content.Shared/.
	/// </summary>
	/// <returns>the loaded assembly, or null if it's missing.</returns>
	private static Assembly? LoadContent(string root)
	{
		var path = Path.Combine(root, "bin", "Content.Shared", "Content.Shared.dll");
		if (!File.Exists(path))
		{
			Console.Error.WriteLine($"Could not find the content DLL at {path}. Build the solution first.");
			return null;
		}

		// A separate load context keeps content apart from the engine.
		// When content asks for a DLL this context doesn't have,
		//	like Engine.Shared, .NET falls back to the default context,
		//	so content gets the engine's own copy and its Component is the same type as ours.
		var context = new AssemblyLoadContext("Content");
		return context.LoadFromAssemblyPath(path);
	}

	/// <summary>
	///		Walks up from the exe's folder until it finds the repo root: the folder containing Resources/Prototypes.
	/// </summary>
	/// <remarks>
	///		The exe lives in Engine/Engine.Client/bin/Debug/net10.0/, so this works
	///		no matter which folder the game was started from.
	/// </remarks>
	private static string? FindRoot()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "Resources", "Prototypes")))
				return dir.FullName;
		}

		return null;
	}
}
