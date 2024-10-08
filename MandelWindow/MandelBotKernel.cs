using Amplifier.OpenCL;

namespace MandelWindow
{
    class MandelBotKernel : OpenCLFunctions
    {
        [OpenCLKernel]
        void IterCount([Global] int[] output, float mandelCenterX, float mandelCenterY,
                       float mandelWidth, float mandelHeight, int width, int height, int mandelDepth)
        {
            //hämta global id av tråden
            int i = get_global_id(0);

            //beräkna vilken rad och kolumn tråden tillhör
            int row = i / width;
            int column = i % width;

            //beräkna mandelbrot set för aktuell tråd, ingen skillnad mot UpdateMandel()
            if (row < height && column < width)
            {

                double cx = mandelCenterX - mandelWidth + column * (mandelWidth * 2.0f / width);
                double cy = mandelCenterY - mandelHeight + row * (mandelHeight * 2.0f / height);

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
                output[i] = result;
            }
        }
    }
}