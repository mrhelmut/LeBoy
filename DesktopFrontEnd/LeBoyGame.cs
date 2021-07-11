using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LeBoyLib;
using System.Threading;
using System.IO;
using System.Diagnostics;
using Microsoft.Xna.Framework.Audio;

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

        private DynamicSoundEffectInstance _channel1 = new DynamicSoundEffectInstance(GBZ80.SPUSampleRate, AudioChannels.Stereo);
        private DynamicSoundEffectInstance _channel2 = new DynamicSoundEffectInstance(GBZ80.SPUSampleRate, AudioChannels.Stereo);
        private DynamicSoundEffectInstance _channel3 = new DynamicSoundEffectInstance(GBZ80.SPUSampleRate, AudioChannels.Stereo);
        private DynamicSoundEffectInstance _channel4 = new DynamicSoundEffectInstance(GBZ80.SPUSampleRate, AudioChannels.Stereo);

        private byte[] _audioBuffer1 = new byte[1000000];
        private int _bufferLength1 = 0;
        private byte[] _audioBuffer2 = new byte[1000000];
        private int _bufferLength2 = 0;
        private byte[] _audioBuffer3 = new byte[1000000];
        private int _bufferLength3 = 0;
        private byte[] _audioBuffer4 = new byte[1000000];
        private int _bufferLength4 = 0;

        public LeBoyGame(string romPath)
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            _romFilePath = romPath;



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
            _graphics.PreferredBackBufferWidth = 160 * 4;
            _graphics.PreferredBackBufferHeight = 144 * 4;
            _graphics.ApplyChanges();

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

            _channel1.Play();
            _channel2.Play();
            _channel3.Play();
            _channel4.Play();
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

            lock (_channel1lock)
            {
                if (_bufferLength1 > 0)
                {
                    byte[] buffer = new byte[_bufferLength1];
                    System.Array.Copy(_audioBuffer1, buffer, _bufferLength1);
                    _channel1.SubmitBuffer(buffer);
                    _bufferLength1 = 0;
                }
            }
            lock(_channel2lock)
            {
                if (_bufferLength2 > 0)
                {
                    byte[] buffer = new byte[_bufferLength2];
                    System.Array.Copy(_audioBuffer2, buffer, _bufferLength2);
                    _channel2.SubmitBuffer(buffer);
                    _bufferLength2 = 0;
                }
            }
            lock (_channel3lock)
            {
                if (_bufferLength3 > 0)
                {
                    byte[] buffer = new byte[_bufferLength3];
                    System.Array.Copy(_audioBuffer3, buffer, _bufferLength3);
                    _channel3.SubmitBuffer(buffer);
                    _bufferLength3 = 0;
                }
            }
            lock (_channel4lock)
            {
                if (_bufferLength4 > 0)
                {
                    byte[] buffer = new byte[_bufferLength4];
                    System.Array.Copy(_audioBuffer4, buffer, _bufferLength4);
                    _channel4.SubmitBuffer(buffer);
                    _bufferLength4 = 0;
                }
            }

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
        private object _channel1lock = new object();
        private object _channel2lock = new object();
        private object _channel3lock = new object();
        private object _channel4lock = new object();

        private void EmulatorWork()
        {
            double emulationElapsed = 0.0f;
            double lastElapsedTime = 0.0f;

            Stopwatch s = new Stopwatch();
            s.Start();

            while (_keepEmulatorRunning)
            {
                // timer handling
                // note: there's nothing quite reliable / precise enough in cross-platform .NET
                // so this is quite hack-ish / dirty
                if (emulationElapsed <= 0.0f)
                {
                    uint cycles = _emulator.DecodeAndDispatch();
                    emulationElapsed += (cycles * Stopwatch.Frequency) / GBZ80.ClockSpeed; // host cpu ticks elapsed

                    if (_emulator.Channel1Samples > 0)
                    {
                        lock (_channel1lock)
                        {
                            for (int i = 0; i < _emulator.Channel1Samples; i += 2)
                            {
                                _audioBuffer1[_bufferLength1] = (byte)(_emulator.Channel1Buffer[i + 1] & 0x00FF); // low left
                                _audioBuffer1[_bufferLength1 + 1] = (byte)((_emulator.Channel1Buffer[i + 1] & 0xFF00) >> 8); // high left

                                _audioBuffer1[_bufferLength1 + 2] = (byte)(_emulator.Channel1Buffer[i] & 0x00FF); // low right
                                _audioBuffer1[_bufferLength1 + 3] = (byte)((_emulator.Channel1Buffer[i] & 0xFF00) >> 8); // high right
                                _bufferLength1 += 4;
                            }
                            _emulator.Channel1Samples = 0;
                        }
                    }

                    if (_emulator.Channel2Samples > 0)
                    {
                        lock (_channel2lock)
                        {
                            for (int i = 0; i < _emulator.Channel2Samples; i += 2)
                            {
                                _audioBuffer2[_bufferLength2 ] = (byte)(_emulator.Channel2Buffer[i + 1] & 0x00FF); // low left
                                _audioBuffer2[_bufferLength2 + 1] = (byte)((_emulator.Channel2Buffer[i + 1] & 0xFF00) >> 8); // high left

                                _audioBuffer2[_bufferLength2 + 2] = (byte)(_emulator.Channel2Buffer[i] & 0x00FF); // low right
                                _audioBuffer2[_bufferLength2 + 3] = (byte)((_emulator.Channel2Buffer[i] & 0xFF00) >> 8); // high right
                                _bufferLength2 += 4;
                            }
                            _emulator.Channel2Samples = 0;
                        }
                    }

                    if (_emulator.Channel3Samples > 0)
                    {
                        lock (_channel3lock)
                        {
                            for (int i = 0; i < _emulator.Channel3Samples; i += 2)
                            {
                                _audioBuffer3[_bufferLength3] = (byte)(_emulator.Channel3Buffer[i + 1] & 0x00FF); // low left
                                _audioBuffer3[_bufferLength3 + 1] = (byte)((_emulator.Channel3Buffer[i + 1] & 0xFF00) >> 8); // high left

                                _audioBuffer3[_bufferLength3 + 2] = (byte)(_emulator.Channel3Buffer[i] & 0x00FF); // low right
                                _audioBuffer3[_bufferLength3 + 3] = (byte)((_emulator.Channel3Buffer[i] & 0xFF00) >> 8); // high right
                                _bufferLength3 += 4;
                            }
                            _emulator.Channel3Samples = 0;
                        }
                    }

                    if (_emulator.Channel4Samples > 0)
                    {
                        lock (_channel4lock)
                        {
                            for (int i = 0; i < _emulator.Channel4Samples; i += 2)
                            {
                                _audioBuffer4[_bufferLength4] = (byte)(_emulator.Channel4Buffer[i + 1] & 0x00FF); // low left
                                _audioBuffer4[_bufferLength4 + 1] = (byte)((_emulator.Channel4Buffer[i + 1] & 0xFF00) >> 8); // high left

                                _audioBuffer4[_bufferLength4 + 2] = (byte)(_emulator.Channel4Buffer[i] & 0x00FF); // low right
                                _audioBuffer4[_bufferLength4 + 3] = (byte)((_emulator.Channel4Buffer[i] & 0xFF00) >> 8); // high right
                                _bufferLength4 += 4;
                            }
                            _emulator.Channel4Samples = 0;
                        }
                    }
                }

                long elapsed = s.ElapsedTicks;
                emulationElapsed -= elapsed - lastElapsedTime;
                lastElapsedTime = elapsed;

                if (s.ElapsedTicks > Stopwatch.Frequency) // dirty restart every seconds to not loose too many precision
                {
                    s.Restart();
                    lastElapsedTime -= Stopwatch.Frequency;
                }
            }
        }
    }
}
