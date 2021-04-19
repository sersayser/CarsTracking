﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace CarsTracking
{
    /// <summary>
    /// Detects changes between frames and renders their bounding boxes
    /// </summary>
    public class MotionDetector
    {
        private const int COLOR_DIFFERENCE_THRESHOLD = 40;
        private const int AREA_DIFFERENCE_THRESHOLD = 3;
        private const int MIN_BBOX_SIZE = 8;
        private const int SCALED_WIDTH = 256;

        private Bitmap scaledFrame = null;
        private byte[,] previousFrameMatrix = null;
        private float frameRate;
        private float aspectRatio;
        private float scaleX, scaleY;
        private int scaledWidth = SCALED_WIDTH;
        private int scaledHeight = 0;
        private Rectangle scaledFrameRect;

        public MotionDetector(int width, int height, float frameRate)
        {
            this.frameRate = frameRate;

            aspectRatio = width / (float)height;
            scaledHeight = (int)(scaledWidth / aspectRatio);

            scaleX = width / (float)scaledWidth;
            scaleY = height / (float)scaledHeight;
        }

        /// <summary>
        /// Compares current frame with the previous to detect moved regions and render their bounding boxes
        /// </summary>
        /// <param name="sourceFrame">Current frame bitmap</param>
        public void ProcessFrame(Bitmap sourceFrame)
        {
            if (scaledFrame == null)
            {
                scaledFrame = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format24bppRgb);
                scaledFrameRect = new Rectangle(0, 0, scaledWidth, scaledHeight);
            }

            // scale source frame to lower resolution
            // this removes some noise and speedups computations
            using (Graphics g = Graphics.FromImage(scaledFrame))
            {
                g.DrawImage(sourceFrame, scaledFrameRect);
            }

            // convert scaled frame into array of 8bit grayscale pixels
            byte[,] currentFrameMatrix = GetMatrixFromBitmap(scaledFrame);

            if (previousFrameMatrix != null)
            {
                // perform comparison between current and previous frame
                List<Rectangle> differences = CompareMatrices(currentFrameMatrix, previousFrameMatrix);

                GroupDifferences(differences);
                RemoveInvalidBBoxes(differences);
                RenderBBoxes(sourceFrame, differences);
            }

            previousFrameMatrix = currentFrameMatrix;
        }

        /// <summary>
        /// Converts 24rgb bitmap into two-dimensional array of pixels with 8 bit per pixel grayscale
        /// to speedup and optimize computations
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        private unsafe byte[,] GetMatrixFromBitmap(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            byte[,] matrix = new byte[rect.Height, rect.Width];

            BitmapData bmpData = scaledFrame.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* linePtr = (byte*)bmpData.Scan0;
            for (int row = 0; row < bmpData.Height; row++)
            {
                byte* ptr = linePtr;
                for (int col = 0; col < bmpData.Width; col++)
                {
                    byte color = (byte)((ptr[0] + ptr[1] + ptr[2]) / 3);
                    matrix[row, col] = color;
                    ptr += 3;
                }
                linePtr += bmpData.Stride;
            }
            scaledFrame.UnlockBits(bmpData);
            return matrix;
        }

        /// <summary>
        /// Removes noise bboxes generated by small movements, shades, e.t.c.
        /// </summary>
        /// <param name="differences"></param>
        private void RemoveInvalidBBoxes(List<Rectangle> differences)
        {
            for (int index = 0; index < differences.Count; index++)
            {
                if (differences[index].Width < MIN_BBOX_SIZE || differences[index].Height < MIN_BBOX_SIZE)
                {
                    differences.Remove(differences[index]);
                    index = -1;
                }
            }
        }

        /// <summary>
        /// Checks whether two rectanges are closer to each other than specific threshold
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private bool IsNeighbour(Rectangle a, Rectangle b)
        {
            return (a.X - AREA_DIFFERENCE_THRESHOLD < b.X && a.X + a.Width + AREA_DIFFERENCE_THRESHOLD > b.X ||
                    b.X - AREA_DIFFERENCE_THRESHOLD < a.X && b.X + b.Width + AREA_DIFFERENCE_THRESHOLD > a.X) &&
                   (a.Y - AREA_DIFFERENCE_THRESHOLD < b.Y && a.Y + a.Height + AREA_DIFFERENCE_THRESHOLD > b.Y ||
                    b.Y - AREA_DIFFERENCE_THRESHOLD < a.Y && b.Y + b.Height + AREA_DIFFERENCE_THRESHOLD > a.Y);
        }

        /// <summary>
        /// Combines two rectanges into one
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private Rectangle CombineRectangles(Rectangle a, Rectangle b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int width = Math.Max(a.X + a.Width, b.X + b.Width) - x;
            int height = Math.Max(a.Y + a.Height, b.Y + b.Height) - y;
            return new Rectangle(x, y, width, height);
        }

        /// <summary>
        /// Searches for neighbour rectangles and merges them together
        /// </summary>
        /// <param name="rect"></param>
        private void GroupDifferences(List<Rectangle> rect)
        {
            bool repeat;
            do
            {
                repeat = false;
                for (int index1 = 0; index1 < rect.Count && !repeat; index1++)
                {
                    for (int index2 = 0; index2 < rect.Count && !repeat; index2++)
                    {
                        if (index1 != index2 && IsNeighbour(rect[index1], rect[index2]))
                        {
                            rect[index1] = CombineRectangles(rect[index1], rect[index2]);
                            rect.RemoveAt(index2);
                            repeat = true;
                        }
                    }
                }
            } while (repeat);
        }

        /// <summary>
        /// Compares two matrices and generates list of rectangles describing changed regions
        /// </summary>
        /// <param name="currentFrameMatrix"></param>
        /// <param name="previousFrameMatrix"></param>
        /// <returns></returns>
        private List<Rectangle> CompareMatrices(byte[,] currentFrameMatrix, byte[,] previousFrameMatrix)
        {
            int rows = currentFrameMatrix.GetLength(0);
            int cols = currentFrameMatrix.GetLength(1);

            List<Rectangle> result = new List<Rectangle>();
            Rectangle? current = null;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int value = currentFrameMatrix[row, col] - previousFrameMatrix[row, col];
                    if (Math.Abs(value) > COLOR_DIFFERENCE_THRESHOLD)
                    {
                        if (current == null)
                            current = new Rectangle(col, row, 1, 1);
                        else
                        {
                            current = CombineRectangles(current.Value, new Rectangle(col, row, 1, 1));
                        }
                    }
                    else
                    {
                        if (current != null)
                        {
                            result.Add(current.Value);
                            current = null;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Renders bounding boxes surrounding changed/moved parts of frame
        /// </summary>
        /// <param name="sourceFrame">Frame to draw on</param>
        /// <param name="differences">List of bounding boxes</param>
        private void RenderBBoxes(Bitmap sourceFrame, List<Rectangle> differences)
        {
            using (Graphics g = Graphics.FromImage(sourceFrame))
            {
                for (int index = 0; index < differences.Count; index++)
                {
                    Rectangle diff = differences[index];
                    Rectangle rect = new Rectangle(
                        (int)(diff.X * scaleX), (int)(diff.Y * scaleY),
                        (int)(diff.Width * scaleX), (int)(diff.Height * scaleY));
                    g.DrawRectangle(Pens.Red, rect);
                }
            }
        }

    }
}
