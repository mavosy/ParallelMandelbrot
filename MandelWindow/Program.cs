using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Diagnostics;
using Amplifier;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MandelWindow
{

    // Keybindings for running the program:

    // Press P to switch to parallel processing mode, which uses the GPGPU to run the calculations in parallel.
    // Press S to switch back to sequential processing mode, which runs the calculations sequentially (default).

    // Press Z to switch  to zoom measuring mode, which measures the time it takes for a zoom to execute in full.
    // Press F to switch back to fractal measuring mode, which measures the individual steps in a zoom.
    // Press N to switch back to no measuring mode.

    // Press Enter to zoom to fixed test coordinates.


    class Program
    {
        static WriteableBitmap bitmap;
        static Window windows;
        static Image image;
        static Thread mandelThread;

        static OpenCLCompiler compiler;

        static Stopwatch fractalStopwatch = new();
        static Stopwatch zoomInStopwatch = new();

        static StreamWriter writer;

        static volatile bool activeMandelThread = true;

        static double zoomCenterX = -0.16349229306767682;
        static double zoomCenterY = -1.0260970739840185;
        static int stepsCounter = 1;
        static int stepsDirection = 1;

        static double mandelCenterX = 0.0;
        static double mandelCenterY = 0.0;
        static double mandelWidth = 2.0;
        static double mandelHeight = 2.0;

        public static int mandelDepth = 360;

        //Sets coordinates for the automatic zoom (this is dependant on screen size and resolution and will give different results for different computers).
        public const int fixedCoordinateX = 466;
        public const int fixedCoordinateY = 231;

        public static bool isRunningInParallel = false;
        public static bool isZooming = false;

        public static int fractalLogCount = 0;

        // Usual filepath to log file: "\[repsitoryfolder]\MandelWindow\bin\Debug\net7.0-windows\MandelbrotRenderingLog.txt"
        public const string logFilePath = "MandelbrotRenderingLog.txt";
        public enum ProcessingMode
        {
            Sequential,
            Parallel
        }
        public enum MeasurementMode
        {
            None,
            Zoom,
            Fractal
        }

        // Sets default modes
        private static ProcessingMode currentProcessingMode = ProcessingMode.Sequential;
        private static MeasurementMode currentMeasurementMode = MeasurementMode.None;

        [STAThread]
        static void Main(string[] args)
        {
            image = new Image();
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

            windows = new Window();
            windows.Content = image;
            windows.Show();

            bitmap = new WriteableBitmap(
                (int)windows.ActualWidth,
                (int)windows.ActualHeight,
                96,
                96,
                PixelFormats.Bgr32,
                null);

            image.Source = bitmap;

            image.Stretch = Stretch.None;
            image.HorizontalAlignment = HorizontalAlignment.Left;
            image.VerticalAlignment = VerticalAlignment.Top;

            //Sets keyboard focus on the image, to enable KeyDownEvent
            image.Focusable = true;
            Keyboard.Focus(image);

            image.KeyDown += 
                new KeyEventHandler(image_KeyDown);
            image.MouseLeftButtonDown +=
                new MouseButtonEventHandler(image_MouseLeftButtonDown);
            image.MouseRightButtonDown +=
                new MouseButtonEventHandler(image_MouseRightButtonDown);
            image.MouseMove +=
                new MouseEventHandler(image_MouseMove);
            windows.MouseWheel += 
                new MouseWheelEventHandler(window_MouseWheel);
            windows.Closing += 
                Windows_Closing;

            Application app = new Application();

            RunDesignatedMandelUpdate();

            mandelThread = new Thread(() =>
            {
                while (activeMandelThread)
                {
                    if (stepsCounter > 0)
                    {
                        if (!isZooming)
                        {
                            isZooming = true;
                            zoomInStopwatch.Restart();
                        }

                        stepsCounter--;
                        if (stepsDirection == 1)
                        {
                            mandelCenterX = (mandelCenterX * 0.8 + zoomCenterX * 0.2);
                            mandelCenterY = (mandelCenterY * 0.8 + zoomCenterY * 0.2);
                            mandelWidth *= 0.9;
                            mandelHeight *= 0.9;
                            mandelDepth += 2;
                        }
                        else if (stepsDirection == -1)
                        {
                            mandelCenterX = (mandelCenterX * 0.8 + zoomCenterX * 0.2);
                            mandelCenterY = (mandelCenterY * 0.8 + zoomCenterY * 0.2);
                            mandelWidth *= 1.1;
                            mandelHeight *= 1.1;
                            mandelDepth -= 2;
                        }
                        RunDesignatedMandelUpdate();
                    }
                    else if (stepsCounter == 0 && isZooming)
                    {
                        zoomInStopwatch.Stop();
                        isZooming = false;
                        WriteMeasurementToLogFile(zoomInStopwatch.ElapsedMilliseconds);
                    }
                    Thread.Sleep(1);
                }
            });

            mandelThread.Start();

            app.Run();
        }

        /// <summary>
        /// Sets keybindings for toggling between sequential and parallel processing, and for running zoom x100
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void image_KeyDown(object sender, KeyEventArgs e)
        {
            Key pressedKey = e.Key;
            switch (pressedKey)  
            {
                case Key.P:
                    SetProcessingModeToParallel();
                    break;
                case Key.S:
                    SetProcessingModeToSequential();
                    break;
                case Key.Z:
                    SetMeasureModeToZoom();
                    break;
                case Key.F:
                    SetMeasureModeToFractal();
                    break;
                case Key.N:
                    SetMeasureModeToNone();
                    break;
                case Key.Enter:
                    ZoomFixedCoordinates();
                    break;
                default:
                    break;
            }
        }

        private static void Windows_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            activeMandelThread = false;
            mandelThread.Join();
        }

        static void image_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            int column = (int)e.GetPosition(image).X;
            int row = (int)e.GetPosition(image).Y;

            zoomCenterX = mandelCenterX - mandelWidth + column * ((mandelWidth * 2.0) / bitmap.PixelWidth);
            zoomCenterY = mandelCenterY - mandelHeight + row * ((mandelHeight * 2.0) / bitmap.PixelHeight);

            stepsDirection = -1;
            stepsCounter = 100;
        }

        static void image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            int column = (int)e.GetPosition(image).X;
            int row = (int)e.GetPosition(image).Y;

            zoomCenterX = mandelCenterX - mandelWidth + column * ((mandelWidth * 2.0) / bitmap.PixelWidth);
            zoomCenterY = mandelCenterY - mandelHeight + row * ((mandelHeight * 2.0) / bitmap.PixelHeight);

            stepsDirection = 1;
            stepsCounter = 100;
        }

        static void window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int column = (int)e.GetPosition(image).X;
            int row = (int)e.GetPosition(image).Y;

            if (e.Delta > 0)
            {
                mandelCenterX = mandelCenterX - mandelWidth + column * ((mandelWidth * 2.0) / bitmap.PixelWidth);
                mandelCenterY = mandelCenterY - mandelHeight + row * ((mandelHeight * 2.0) / bitmap.PixelHeight);
                mandelWidth /= 2.0;
                mandelHeight /= 2.0;
            }
            else
            {
                mandelCenterX = mandelCenterX - mandelWidth + column * ((mandelWidth * 2.0) / bitmap.PixelWidth);
                mandelCenterY = mandelCenterY - mandelHeight + row * ((mandelHeight * 2.0) / bitmap.PixelHeight);
                mandelWidth *= 2.0;
                mandelHeight *= 2.0;
            }
            RunDesignatedMandelUpdate();
        }

        static void image_MouseMove(object sender, MouseEventArgs e)
        {
            int column = (int)e.GetPosition(image).X;
            int row = (int)e.GetPosition(image).Y;

            double mouseCenterX = mandelCenterX - mandelWidth + column * ((mandelWidth * 2.0) / bitmap.PixelWidth);
            double mouseCenterY = mandelCenterY - mandelHeight + row * ((mandelHeight * 2.0) / bitmap.PixelHeight);

            windows.Title = $"Coords: X:{mouseCenterX} Y:{mouseCenterY}. {ProcessingModeToString()} (P=Parallel,S=Sequential). Measure: {MeasureModeToString()}. Z=Zoom,F=Fractal,N=None. Press Enter to run!";
        }

        public static void UpdateMandel()
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                try
                {
                    // Reserve the back buffer for updates.
                    bitmap.Lock();

                    if (currentMeasurementMode == MeasurementMode.Fractal) { fractalStopwatch.Restart(); }

                    unsafe
                    {
                        for (int row = 0; row < bitmap.PixelHeight; row++)
                        {
                            for (int column = 0; column < bitmap.PixelWidth; column++)
                            {
                                // Get a pointer to the back buffer.
                                IntPtr pBackBuffer = bitmap.BackBuffer;

                                // Find the address of the pixel to draw.
                                pBackBuffer += row * bitmap.BackBufferStride;
                                pBackBuffer += column * 4;

                                int light = IterCount(mandelCenterX - mandelWidth + column * ((mandelWidth * 2.0) / bitmap.PixelWidth), mandelCenterY - mandelHeight + row * ((mandelHeight * 2.0) / bitmap.PixelHeight));

                                int R, G, B;
                                HsvToRgb(light, 1.0, light < mandelDepth ? 1.0 : 0.0, out R, out G, out B);

                                // Compute the pixel's color.
                                int color_data = R << 16; // R
                                color_data |= G << 8;   // G
                                color_data |= B << 0;   // B

                                // Assign the color data to the pixel.
                                *((int*)pBackBuffer) = color_data;
                            }
                        }
                    }

                    // Specify the area of the bitmap that changed.
                    bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));

                    if (currentMeasurementMode == MeasurementMode.Fractal) 
                    {
                        fractalStopwatch.Stop();
                        WriteMeasurementToLogFile(fractalStopwatch.ElapsedMilliseconds);
                    }
                }
                finally
                {
                    // Release the back buffer and make it available for display.
                    bitmap.Unlock();
                }
            }));
        }

        /// <summary>
        /// Beräknar Mandelbrot set parallellt med GPU (OpenCL)
        /// </summary>
        public static void UpdateMandelParallel()
        {
            //initierar en OpenCLCompiler
            //compilern sköter context, device, kernel, commandqueue
            if (compiler == null)
            {
                compiler = new OpenCLCompiler();
                compiler.UseDevice(0); // Selects first GPU device
                compiler.CompileKernel(typeof(MandelBotKernel));
            }
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                try
                {
                    // Reserve the back buffer for updates.
                    bitmap.Lock();

                    //inparameter till Mandelbrot set
                    int pixelWidth = bitmap.PixelWidth;
                    int pixelHeight = bitmap.PixelHeight;
                    int[] output = new int[pixelWidth * pixelHeight];

                    //kompilerar och exekuverar kerneln
                    compiler.Execute("IterCount", new object[] { output, mandelCenterX, mandelCenterY, mandelWidth, mandelHeight, pixelWidth, pixelHeight, mandelDepth });

                    if (currentMeasurementMode == MeasurementMode.Fractal) { fractalStopwatch.Restart(); }

                    unsafe
                    {
                        // Itererar output som är en array med Mandelbrot set per pixel
                        // sätter en färg per pixel genom HsvToRgb
                        for (int i = 0; i < output.Length; i++)
                        {
                            int row = i / pixelWidth;
                            int column = i % pixelWidth;

                            // Get a pointer to the back buffer.
                            IntPtr pBackBuffer = bitmap.BackBuffer;

                            // Find the address of the pixel to draw.
                            pBackBuffer += row * bitmap.BackBufferStride;
                            pBackBuffer += column * 4;

                            int light = output[i];

                            int R, G, B;
                            HsvToRgb(light, 1.0, light < mandelDepth ? 1.0 : 0.0, out R, out G, out B);

                            // Compute the pixel's color.
                            int color_data = R << 16; // R
                            color_data |= G << 8;   // G
                            color_data |= B << 0;   // B

                            // Assign the color data to the pixel.
                            *((int*)pBackBuffer) = color_data;
                        }
                    }
                    // Specify the area of the bitmap that changed.
                    bitmap.AddDirtyRect(new Int32Rect(0, 0, pixelWidth, pixelHeight));

                    if (currentMeasurementMode == MeasurementMode.Fractal)
                    {
                        fractalStopwatch.Stop();
                        WriteMeasurementToLogFile(fractalStopwatch.ElapsedMilliseconds);
                    }
                }
                finally
                {
                    // Release the back buffer and make it available for display.
                    bitmap.Unlock();
                }
            }));
        }

        public static int IterCount(double cx, double cy)
        {
            int result = 0;
            double x = 0.0f;
            double y = 0.0f;
            double xx = 0.0f, yy = 0.0;
            while (xx + yy <= 4.0 && result < mandelDepth) // are we out of control disk?
            {
                xx = x * x;
                yy = y * y;
                double xtmp = xx - yy + cx;
                y = 2.0f * x * y + cy; // computes z^2 + c
                x = xtmp;
                result++;
            }
            return result;
        }

        static void HsvToRgb(double h, double S, double V, out int r, out int g, out int b)
        {
            double H = h;
            while (H < 0) { H += 360; };
            while (H >= 360) { H -= 360; };
            double R, G, B;
            if (V <= 0)
            { R = G = B = 0; }
            else if (S <= 0)
            {
                R = G = B = V;
            }
            else
            {
                double hf = H / 60.0;
                int i = (int)Math.Floor(hf);
                double f = hf - i;
                double pv = V * (1 - S);
                double qv = V * (1 - S * f);
                double tv = V * (1 - S * (1 - f));
                switch (i)
                {

                    // Red is the dominant color

                    case 0:
                        R = V;
                        G = tv;
                        B = pv;
                        break;

                    // Green is the dominant color

                    case 1:
                        R = qv;
                        G = V;
                        B = pv;
                        break;
                    case 2:
                        R = pv;
                        G = V;
                        B = tv;
                        break;

                    // Blue is the dominant color

                    case 3:
                        R = pv;
                        G = qv;
                        B = V;
                        break;
                    case 4:
                        R = tv;
                        G = pv;
                        B = V;
                        break;

                    // Red is the dominant color

                    case 5:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // Just in case we overshoot on our math by a little, we put these here. Since its a switch it won't slow us down at all to put these here.

                    case 6:
                        R = V;
                        G = tv;
                        B = pv;
                        break;
                    case -1:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // The color is not defined, we should throw an error.

                    default:
                        //LFATAL("i Value error in Pixel conversion, Value is %d", i);
                        R = G = B = V; // Just pretend its black/white
                        break;
                }
            }
            r = Clamp((int)(R * 255.0));
            g = Clamp((int)(G * 255.0));
            b = Clamp((int)(B * 255.0));
        }

        /// <summary>
        /// Clamp a value to 0-255
        /// </summary>
        static int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

        /// <summary>
        /// Writes the measured timespan to a .txt log file
        /// </summary>
        /// <param name="measuredTimeInMs"></param>
        private static void WriteMeasurementToLogFile(long measuredTimeInMs)
        {
            try
            {
                using (writer = new StreamWriter(logFilePath, true))
                {
                    if (currentMeasurementMode == MeasurementMode.Zoom)
                    {
                        writer.WriteLine($"{DateTime.Now} {ProcessingModeToString()} {MeasureModeToString()}: {measuredTimeInMs}");
                    }
                    else if (currentMeasurementMode == MeasurementMode.Fractal)
                    {
                        fractalLogCount++;
                        writer.WriteLine($"{fractalLogCount}. {DateTime.Now} {ProcessingModeToString()} {MeasureModeToString()}: {measuredTimeInMs}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }

        //Setting modes for processing and measurements
        private static void SetProcessingModeToSequential() => currentProcessingMode = ProcessingMode.Sequential;
        private static void SetProcessingModeToParallel() => currentProcessingMode = ProcessingMode.Parallel;
        private static void SetMeasureModeToZoom() => currentMeasurementMode = MeasurementMode.Zoom;
        private static void SetMeasureModeToFractal() => currentMeasurementMode = MeasurementMode.Fractal;
        private static void SetMeasureModeToNone() => currentMeasurementMode = MeasurementMode.None;

        /// <summary>
        /// Writing process mode in windows.Title
        /// </summary>
        /// <returns></returns>
        private static string ProcessingModeToString()
        {
            switch (currentProcessingMode)
            {
                case ProcessingMode.Sequential:
                    return "Sequential";
                case ProcessingMode.Parallel:
                    return "Parallel";
                default:
                    return "NULL";
            }
        }

        /// <summary>
        /// Writing measure mode in windows.Title
        /// </summary>
        /// <returns></returns>
        private static string MeasureModeToString() 
        {
            switch (currentMeasurementMode)
            {
                case MeasurementMode.None:
                    return "None";
                case MeasurementMode.Zoom:
                    return "Zoom";
                case MeasurementMode.Fractal:
                    return "Fractal";
                default:
                    return "NULL";
            }
        }

        /// <summary>
        /// Runs sequential or parallel update of the mandelbrot calculations
        /// </summary>
        public static void RunDesignatedMandelUpdate()
        {
            if (currentProcessingMode == ProcessingMode.Sequential)
            {
                UpdateMandel();
            }
            else if (currentProcessingMode == ProcessingMode.Parallel)
            {
                UpdateMandelParallel();
            }
        }

        /// <summary>
        /// Zooms 100 steps to a fixed coordinate, set by constants fixedCoordinateX and fixedCoordinateY
        /// </summary>
        private static void ZoomFixedCoordinates()
        {
            zoomCenterX = mandelCenterX - mandelWidth + fixedCoordinateX * ((mandelWidth * 2.0) / bitmap.PixelWidth);
            zoomCenterY = mandelCenterY - mandelHeight + fixedCoordinateY * ((mandelHeight * 2.0) / bitmap.PixelHeight);

            stepsDirection = 1;
            stepsCounter = 100;
        }
    }
}