using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LeBoyLib;
using System.Threading;
using System.IO;

namespace DesktopFrontEnd
{
    /// <summary>
    /// This is the main type for your game.
    /// </summary>
    public class LeBoyGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        private string _romFilePath;

        private GBZ80 _emulator = new GBZ80();
        private Thread _emulatorThread;
        private Texture2D _emulatorBackbuffer;
        
        public LeBoyGame(string romPath)
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            _romFilePath = romPath;

            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 720;
            _graphics.ApplyChanges();

            Window.AllowUserResizing = true;
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            // TODO: Add your initialization logic here

            base.Initialize();
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            // Create a new SpriteBatch, which can be used to draw textures.
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _emulatorBackbuffer = new Texture2D(GraphicsDevice, 160, 144);

            // loading a rom and starting emulation
            if (File.Exists(_romFilePath))
            {
                using (FileStream fs = new FileStream(_romFilePath, FileMode.Open))
                {
                    using (BinaryReader br = new BinaryReader(fs))
                    {
                        byte[] rom = new byte[fs.Length];
                        for (int i = 0; i < fs.Length; i++)
                            rom[i] = br.ReadByte();
                        _emulator.Load(rom);
                    }
                }

                // initialize emulator thread
                _keepEmulatorRunning = true;
                _emulatorThread = new Thread(EmulatorWork);
                _emulatorThread.Start();                
            }
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// game-specific content.
        /// </summary>
        protected override void UnloadContent()
        {
            // stopping emulation thread
            if (_emulatorThread != null && _emulatorThread.IsAlive)
                _keepEmulatorRunning = false;
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            GamePadState gamePadState = GamePad.GetState(PlayerIndex.One);

            // inputs
            _emulator.JoypadState[0] = (gamePadState.DPad.Right == ButtonState.Pressed);
            _emulator.JoypadState[1] = (gamePadState.DPad.Left == ButtonState.Pressed);
            _emulator.JoypadState[2] = (gamePadState.DPad.Up == ButtonState.Pressed);
            _emulator.JoypadState[3] = (gamePadState.DPad.Down == ButtonState.Pressed);
            _emulator.JoypadState[4] = (gamePadState.Buttons.B == ButtonState.Pressed);
            _emulator.JoypadState[5] = (gamePadState.Buttons.A == ButtonState.Pressed);
            _emulator.JoypadState[6] = (gamePadState.Buttons.Back == ButtonState.Pressed);
            _emulator.JoypadState[7] = (gamePadState.Buttons.Start == ButtonState.Pressed);

            // upload backbuffer to texture
            byte[] backbuffer = _emulator.GetScreenBuffer();
            if (backbuffer != null)
                _emulatorBackbuffer.SetData<byte>(backbuffer);

            base.Update(gameTime);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            // compute bounds
            Rectangle bounds = GraphicsDevice.Viewport.Bounds;

            float aspectRatio = GraphicsDevice.Viewport.Bounds.Width / (float)GraphicsDevice.Viewport.Bounds.Height;
            float targetAspectRatio = 160.0f / 144.0f;

            if (aspectRatio > targetAspectRatio)
            {
                int targetWidth = (int)(bounds.Height * targetAspectRatio);
                bounds.X = (bounds.Width - targetWidth) / 2;
                bounds.Width = targetWidth;
            }
            else if (aspectRatio < targetAspectRatio)
            {
                int targetHeight = (int)(bounds.Width / targetAspectRatio);
                bounds.Y = (bounds.Height - targetHeight) / 2;
                bounds.Height = targetHeight;
            }

            // draw backbuffer
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            _spriteBatch.Draw(_emulatorBackbuffer, bounds, Color.White);
            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private bool _keepEmulatorRunning = false;
        
        private void EmulatorWork()
        {
            double cpuSecondsElapsed = 0.0f;

            MicroStopwatch s = new MicroStopwatch();
            s.Start();

            while (_keepEmulatorRunning)
            {
                uint cycles = _emulator.DecodeAndDispatch();

                // timer handling
                // note: there's nothing quite reliable / precise enough in cross-platform .Net
                // so this is quite hack-ish / dirty
                cpuSecondsElapsed += cycles / GBZ80.ClockSpeed;

                double realSecondsElapsed = s.ElapsedMicroseconds * 1000000;

                if (realSecondsElapsed - cpuSecondsElapsed > 0.0) // dirty wait
                {
                    realSecondsElapsed = s.ElapsedMicroseconds * 1000000;
                }

                if (s.ElapsedMicroseconds > 1000000) // dirty restart every seconds to not loose too many precision
                {
                    s.Restart();
                    cpuSecondsElapsed -= 1.0;
                }
            }
        }
    }
}
