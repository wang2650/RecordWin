﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RecordWin
{
    public class VideoStream : AviStream
    {
        /// <summary>handle for AVIStreamGetFrame</summary>
        private int getFrameObject;

        public int FrameSize { get; private set; }

        protected double frameRate;
        public double FrameRate => frameRate;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public Int16 CountBitsPerPixel { get; private set; }

        /// <summary>count of frames in the stream</summary>
        protected int countFrames = 0;
        public int CountFrames => countFrames;

        /// <summary>Palette for indexed frames</summary>
        protected Avi.RGBQUAD[] palette;
        public Avi.RGBQUAD[] Palette => palette;

        /// <summary>initial frame index</summary>
        /// <remarks>Added by M. Covington</remarks>
        protected int firstFrame = 0;
        public int FirstFrame => firstFrame;

        private Avi.AVICOMPRESSOPTIONS compressOptions;
        public Avi.AVICOMPRESSOPTIONS CompressOptions => compressOptions;
        public Avi.AVISTREAMINFO StreamInfo => GetStreamInfo(aviStream);

        /// <summary>Initialize an empty VideoStream</summary>
        /// <param name="aviFile">The file that contains the stream</param>
        /// <param name="writeCompressed">true: Create a compressed stream before adding frames</param>
        /// <param name="frameRate">Frames per second</param>
        /// <param name="frameSize">Size of one frame in bytes</param>
        /// <param name="width">Width of each image</param>
        /// <param name="height">Height of each image</param>
        /// <param name="format">PixelFormat of the images</param>
        public VideoStream(int aviFile, bool writeCompressed, double frameRate, int frameSize, int width, int height, PixelFormat format)
        {
            this.aviFile = aviFile;
            this.writeCompressed = writeCompressed;
            this.frameRate = frameRate;
            FrameSize = frameSize;
            Width = width;
            Height = height;
            CountBitsPerPixel = ConvertPixelFormatToBitCount(format);
            firstFrame = 0;

            CreateStream();
        }

        /// <summary>Initialize a new VideoStream and add the first frame</summary>
        /// <param name="aviFile">The file that contains the stream</param>
        /// <param name="writeCompressed">true: create a compressed stream before adding frames</param>
        /// <param name="frameRate">Frames per second</param>
        /// <param name="firstFrame">Image to write into the stream as the first frame</param>
        public VideoStream(int aviFile, bool writeCompressed, double frameRate, Bitmap firstFrame)
        {
            Initialize(aviFile, writeCompressed, frameRate, firstFrame);
            CreateStream();
            AddFrame(firstFrame);
        }

        /// <summary>Initialize a new VideoStream and add the first frame</summary>
        /// <param name="aviFile">The file that contains the stream</param>
        /// <param name="writeCompressed">true: create a compressed stream before adding frames</param>
        /// <param name="frameRate">Frames per second</param>
        /// <param name="firstFrame">Image to write into the stream as the first frame</param>
        public VideoStream(int aviFile, Avi.AVICOMPRESSOPTIONS compressOptions, double frameRate, Bitmap firstFrame)
        {
            Initialize(aviFile, true, frameRate, firstFrame);
            CreateStream(compressOptions);
            AddFrame(firstFrame);
        }

        /// <summary>Initialize a VideoStream for an existing stream</summary>
        /// <param name="aviFile">The file that contains the stream</param>
        /// <param name="aviStream">An IAVISTREAM from [aviFile]</param>
        public VideoStream(int aviFile, IntPtr aviStream)
        {
            this.aviFile = aviFile;
            this.aviStream = aviStream;
            Avi.AVISTREAMINFO streamInfo = GetStreamInfo(aviStream);

            //Avi.BITMAPINFOHEADER bih = new Avi.BITMAPINFOHEADER();
            //int size = Marshal.SizeOf(bih);
            Avi.BITMAPINFO bih = new Avi.BITMAPINFO();
            int size = Marshal.SizeOf(bih.bmiHeader);
            Avi.AVIStreamReadFormat(aviStream, 0, ref bih, ref size);

            if (bih.bmiHeader.biBitCount < 24)
            {
                size = Marshal.SizeOf(bih.bmiHeader) + Avi.PALETTE_SIZE;
                Avi.AVIStreamReadFormat(aviStream, 0, ref bih, ref size);
                CopyPalette(bih.bmiColors);
            }

            frameRate = streamInfo.dwRate / (float)streamInfo.dwScale;
            Width = (int)streamInfo.rcFrame.right;
            Height = (int)streamInfo.rcFrame.bottom;
            FrameSize = bih.bmiHeader.biSizeImage;
            CountBitsPerPixel = bih.bmiHeader.biBitCount;
            firstFrame = Avi.AVIStreamStart(aviStream.ToInt32());
            countFrames = Avi.AVIStreamLength(aviStream.ToInt32());
        }

        /// <summary>Copy a palette</summary>
        /// <param name="template">Original palette</param>
        private void CopyPalette(ColorPalette template)
        {
            palette = new Avi.RGBQUAD[template.Entries.Length];

            for (int n = 0; n < palette.Length; n++)
            {
                if (n < template.Entries.Length)
                {
                    palette[n].rgbRed = template.Entries[n].R;
                    palette[n].rgbGreen = template.Entries[n].G;
                    palette[n].rgbBlue = template.Entries[n].B;
                }
                else
                {
                    palette[n].rgbRed = 0;
                    palette[n].rgbGreen = 0;
                    palette[n].rgbBlue = 0;
                }
            }
        }

        /// <summary>Copy a palette</summary>
        /// <param name="template">Original palette</param>
        private void CopyPalette(Avi.RGBQUAD[] template)
        {
            palette = new Avi.RGBQUAD[template.Length];

            for (int n = 0; n < palette.Length; n++)
            {
                if (n < template.Length)
                {
                    palette[n].rgbRed = template[n].rgbRed;
                    palette[n].rgbGreen = template[n].rgbGreen;
                    palette[n].rgbBlue = template[n].rgbBlue;
                }
                else
                {
                    palette[n].rgbRed = 0;
                    palette[n].rgbGreen = 0;
                    palette[n].rgbBlue = 0;
                }
            }
        }

        /// <summary>Initialize a new VideoStream</summary>
        /// <remarks>Used only by constructors</remarks>
        /// <param name="aviFile">The file that contains the stream</param>
        /// <param name="writeCompressed">true: create a compressed stream before adding frames</param>
        /// <param name="frameRate">Frames per second</param>
        /// <param name="firstFrame">Image to write into the stream as the first frame</param>
        private void Initialize(int aviFile, bool writeCompressed, double frameRate, Bitmap firstFrameBitmap)
        {
            this.aviFile = aviFile;
            this.writeCompressed = writeCompressed;
            this.frameRate = frameRate;
            firstFrame = 0;

            CopyPalette(firstFrameBitmap.Palette);

            BitmapData bmpData = firstFrameBitmap.LockBits(new Rectangle(
                0, 0, firstFrameBitmap.Width, firstFrameBitmap.Height),
                ImageLockMode.ReadOnly, firstFrameBitmap.PixelFormat);

            FrameSize = bmpData.Stride * bmpData.Height;
            Width = firstFrameBitmap.Width;
            Height = firstFrameBitmap.Height;
            CountBitsPerPixel = ConvertPixelFormatToBitCount(firstFrameBitmap.PixelFormat);

            firstFrameBitmap.UnlockBits(bmpData);
        }

        /// <summary>Get the count of bits per pixel from a PixelFormat value</summary>
        /// <param name="format">One of the PixelFormat members beginning with "Format..." - all others are not supported</param>
        /// <returns>bit count</returns>
        private Int16 ConvertPixelFormatToBitCount(PixelFormat format)
        {
            String formatName = format.ToString();
            if (formatName.Substring(0, 6) != "Format")
            {
                throw new Exception("Unknown pixel format: " + formatName);
            }

            formatName = formatName.Substring(6, 2);
            Int16 bitCount = 0;
            if (Char.IsNumber(formatName[1]))
            {   //16, 32, 48
                bitCount = Int16.Parse(formatName);
            }
            else
            {                               //4, 8
                bitCount = Int16.Parse(formatName[0].ToString());
            }

            return bitCount;
        }

        private Avi.AVISTREAMINFO GetStreamInfo(IntPtr aviStream)
        {
            Avi.AVISTREAMINFO streamInfo = new Avi.AVISTREAMINFO();
            int result = Avi.AVIStreamInfo(StreamPointer, ref streamInfo, Marshal.SizeOf(streamInfo));
            if (result != 0)
            {
                throw new Exception("Exception in VideoStreamInfo: " + result.ToString());
            }
            return streamInfo;
        }

        private void GetRateAndScale(ref double frameRate, ref int scale)
        {
            scale = 1;
            while (frameRate != (long)frameRate)
            {
                frameRate = frameRate * 10;
                scale *= 10;
            }
        }

        /// <summary>Create a new stream</summary>
        private void CreateStreamWithoutFormat()
        {
            int scale = 1;
            double rate = frameRate;
            GetRateAndScale(ref rate, ref scale);

            Avi.AVISTREAMINFO strhdr = new Avi.AVISTREAMINFO();
            strhdr.fccType = Avi.mmioStringToFOURCC("vids", 0);
            strhdr.fccHandler = Avi.mmioStringToFOURCC("CVID", 0);
            strhdr.dwFlags = 0;
            strhdr.dwCaps = 0;
            strhdr.wPriority = 0;
            strhdr.wLanguage = 0;
            strhdr.dwScale = scale;
            strhdr.dwRate = (int)rate; // Frames per Second
            strhdr.dwStart = 0;
            strhdr.dwLength = 0;
            strhdr.dwInitialFrames = 0;
            strhdr.dwSuggestedBufferSize = FrameSize; //height_ * stride_;
            strhdr.dwQuality = -1;        //default
            strhdr.dwSampleSize = 0;
            strhdr.rcFrame.top = 0;
            strhdr.rcFrame.left = 0;
            strhdr.rcFrame.bottom = (uint)Height;
            strhdr.rcFrame.right = (uint)Width;
            strhdr.dwEditCount = 0;
            strhdr.dwFormatChangeCount = 0;
            strhdr.szName = new UInt16[64];

            int result = Avi.AVIFileCreateStream(aviFile, out aviStream, ref strhdr);

            if (result != 0)
            {
                throw new Exception("Exception in AVIFileCreateStream: " + result.ToString());
            }
        }

        /// <summary>Create a new stream</summary>
        private void CreateStream()
        {
            CreateStreamWithoutFormat();

            if (writeCompressed)
            {
                CreateCompressedStream();
            }
            else
            {
                //SetFormat(aviStream, 0);
            }
        }

        /// <summary>Create a new stream</summary>
        private void CreateStream(Avi.AVICOMPRESSOPTIONS options)
        {
            CreateStreamWithoutFormat();
            CreateCompressedStream(options);
        }

        /// <summary>Create a compressed stream from an uncompressed stream</summary>
        private void CreateCompressedStream()
        {
            //display the compression options dialog...
            //Avi.AVICOMPRESSOPTIONS_CLASS options = new Avi.AVICOMPRESSOPTIONS_CLASS();
            //options.fccType = (uint)Avi.streamtypeVIDEO;

            //options.lpParms = IntPtr.Zero;
            //options.lpFormat = IntPtr.Zero;
            //Avi.AVISaveOptions(IntPtr.Zero, Avi.ICMF_CHOOSE_KEYFRAME | Avi.ICMF_CHOOSE_DATARATE, 1, ref aviStream, ref options);

            //..or set static options
            /*Avi.AVICOMPRESSOPTIONS opts = new Avi.AVICOMPRESSOPTIONS();
            opts.fccType         = (UInt32)Avi.mmioStringToFOURCC("vids", 0);
            opts.fccHandler      = (UInt32)Avi.mmioStringToFOURCC("CVID", 0);
            opts.fccType = (UInt32)Avi.mmioStringToFOURCC("mrle", 0);
            opts.fccHandler = (UInt32)Avi.mmioStringToFOURCC("MRLE", 0);
			
            opts.dwKeyFrameEvery = 0;
            opts.dwQuality       = 0;  // 0 .. 10000
            opts.dwFlags         = 0;  // AVICOMRPESSF_KEYFRAMES = 4
            opts.dwBytesPerSecond= 0;
            opts.lpFormat        = new IntPtr(0);
            opts.cbFormat        = 0;
            opts.lpParms         = new IntPtr(0);
            opts.cbParms         = 0;
            opts.dwInterleaveEvery = 0;*/

            //get the compressed stream
            Avi.AVICOMPRESSOPTIONS opts = new Avi.AVICOMPRESSOPTIONS();
            opts.fccType = 0;
            opts.fccHandler = 0x6376736d;
            opts.dwKeyFrameEvery = 25;
            opts.dwQuality = 10000;
            opts.dwFlags = 8;
            opts.dwBytesPerSecond = 0;
            opts.lpFormat = new IntPtr(0);
            opts.cbFormat = 0;
            opts.lpParms = new IntPtr(0);
            opts.cbParms = 4;
            opts.dwInterleaveEvery = 0;

            opts.lpParms = Marshal.AllocHGlobal(sizeof(int));
            //AVISaveOptions(0, 0, 1, ptr_ps, pp);
            //this.compressOptions = options.ToStruct();
            int result = Avi.AVIMakeCompressedStream(out compressedStream, aviStream, ref opts, 0);
            if (result != 0)
            {
                throw new Exception("Exception in AVIMakeCompressedStream: " + result.ToString());
            }

            //Avi.AVISaveOptionsFree(1, ref opts);
            SetFormat(compressedStream, 0);
        }

        /// <summary>Create a compressed stream from an uncompressed stream</summary>
        private void CreateCompressedStream(Avi.AVICOMPRESSOPTIONS options)
        {
            int result = Avi.AVIMakeCompressedStream(out compressedStream, aviStream, ref options, 0);
            if (result != 0)
            {
                throw new Exception("Exception in AVIMakeCompressedStream: " + result.ToString());
            }

            compressOptions = options;

            SetFormat(compressedStream, 0);
        }

        /// <summary>Add one frame to a new stream</summary>
        /// <param name="bmp"></param>
        /// <remarks>
        /// This works only with uncompressed streams,
        /// and compressed streams that have not been saved yet.
        /// Use DecompressToNewFile to edit saved compressed streams.
        /// </remarks>
        public void AddFrame(Bitmap bmp)
        {
            //bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
            bmp.RotateFlip(RotateFlipType.Rotate180FlipX);
            // NEW 2012-11-10
            if (countFrames == 0)
            {
                CopyPalette(bmp.Palette);
                SetFormat(writeCompressed ? compressedStream : StreamPointer, countFrames);
            }

            BitmapData bmpDat = bmp.LockBits(
                new Rectangle(
                0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, bmp.PixelFormat);

            int result = Avi.AVIStreamWrite(writeCompressed ? compressedStream : StreamPointer,
                countFrames, 1,
                bmpDat.Scan0,
                bmpDat.Stride * bmpDat.Height,
                0, 0, 0);

            if (result != 0)
            {
                throw new Exception("Exception in VideoStreamWrite: " + result.ToString());
            }

            bmp.UnlockBits(bmpDat);

            countFrames++;
        }

        /// <summary>Apply a format to a new stream</summary>
        /// <param name="aviStream">The IAVISTREAM</param>
        /// <remarks>
        /// The format must be set before the first frame can be written,
        /// and it cannot be changed later.
        /// </remarks>
        private void SetFormat(IntPtr aviStream, int writePosition)
        {
            Avi.BITMAPINFO bi = new Avi.BITMAPINFO();
            bi.bmiHeader.biWidth = Width;
            bi.bmiHeader.biHeight = Height;
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = CountBitsPerPixel;
            bi.bmiHeader.biSizeImage = FrameSize;
            bi.bmiHeader.biSize = Marshal.SizeOf(bi.bmiHeader);

            if (CountBitsPerPixel < 24)
            {
                bi.bmiHeader.biClrUsed = palette.Length;
                bi.bmiHeader.biClrImportant = palette.Length;
                bi.bmiColors = new Avi.RGBQUAD[palette.Length];
                palette.CopyTo(bi.bmiColors, 0);
                bi.bmiHeader.biSize += bi.bmiColors.Length * Avi.RGBQUAD_SIZE;
            }

            int result = Avi.AVIStreamSetFormat(aviStream, writePosition, ref bi, bi.bmiHeader.biSize);
            if (result != 0) { throw new Exception("Error in VideoStreamSetFormat: " + result.ToString("X")); }
        }

        /// <summary>Prepare for decompressing frames</summary>
        /// <remarks>
        /// This method has to be called before GetBitmap and ExportBitmap.
        /// Release ressources with GetFrameClose.
        /// </remarks>
        public void GetFrameOpen()
        {
            Avi.AVISTREAMINFO streamInfo = GetStreamInfo(StreamPointer);

            //Open frames

            Avi.BITMAPINFOHEADER bih = new Avi.BITMAPINFOHEADER();
            bih.biBitCount = CountBitsPerPixel;
            bih.biClrImportant = 0;
            bih.biClrUsed = 0;
            bih.biCompression = 0;
            bih.biPlanes = 1;
            bih.biSize = Marshal.SizeOf(bih);
            bih.biXPelsPerMeter = 0;
            bih.biYPelsPerMeter = 0;

            // Corrections by M. Covington:
            // If these are pre-set, interlaced video is not handled correctly.
            // Better to give zeroes and let Windows fill them in.
            bih.biHeight = 0; // was (Int32)streamInfo.rcFrame.bottom;
            bih.biWidth = 0; // was (Int32)streamInfo.rcFrame.right;

            // Corrections by M. Covington:
            // Validate the bit count, because some AVI files give a bit count
            // that is not one of the allowed values in a BitmapInfoHeader.
            // Here 0 means for Windows to figure it out from other information.
            if (bih.biBitCount > 24)
            {
                bih.biBitCount = 32;
            }
            else if (bih.biBitCount > 16)
            {
                bih.biBitCount = 24;
            }
            else if (bih.biBitCount > 8)
            {
                bih.biBitCount = 16;
            }
            else if (bih.biBitCount > 4)
            {
                bih.biBitCount = 8;
            }
            else if (bih.biBitCount > 0)
            {
                bih.biBitCount = 4;
            }

            getFrameObject = Avi.AVIStreamGetFrameOpen(StreamPointer, ref bih);

            if (getFrameObject == 0) { throw new Exception("Exception in VideoStreamGetFrameOpen!"); }
        }

        /// <summary>Export a frame into a bitmap</summary>
        /// <param name="position">Position of the frame</param>
        public Bitmap GetBitmap(int position)
        {
            if (position > countFrames)
            {
                throw new Exception("Invalid frame position: " + position);
            }

            Avi.AVISTREAMINFO streamInfo = GetStreamInfo(StreamPointer);

            Avi.BITMAPINFO bih = new Avi.BITMAPINFO();
            int headerSize = Marshal.SizeOf(bih.bmiHeader);

            //Decompress the frame and return a pointer to the DIB
            int dib = Avi.AVIStreamGetFrame(getFrameObject, firstFrame + position);

            //Copy the bitmap header into a managed struct
            bih.bmiColors = palette;
            bih.bmiHeader = (Avi.BITMAPINFOHEADER)Marshal.PtrToStructure(new IntPtr(dib), bih.bmiHeader.GetType());

            if (bih.bmiHeader.biSizeImage < 1)
            {
                throw new Exception("Exception in VideoStreamGetFrame");
            }

            //copy the image			
            int framePaletteSize = bih.bmiHeader.biClrUsed * Avi.RGBQUAD_SIZE;
            byte[] bitmapData = new byte[bih.bmiHeader.biSizeImage];
            IntPtr dibPointer = new IntPtr(dib + Marshal.SizeOf(bih.bmiHeader) + framePaletteSize);
            Marshal.Copy(dibPointer, bitmapData, 0, bih.bmiHeader.biSizeImage);

            //copy bitmap info
            byte[] bitmapInfo = new byte[Marshal.SizeOf(bih)];
            IntPtr ptr = Marshal.AllocHGlobal(bitmapInfo.Length);
            Marshal.StructureToPtr(bih, ptr, false);
            Marshal.Copy(ptr, bitmapInfo, 0, bitmapInfo.Length);
            Marshal.FreeHGlobal(ptr);

            //create file header
            Avi.BITMAPFILEHEADER bfh = new Avi.BITMAPFILEHEADER();
            bfh.bfType = Avi.BMP_MAGIC_COOKIE;
            bfh.bfSize = 55 + bih.bmiHeader.biSizeImage; //size of file as written to disk
            bfh.bfReserved1 = 0;
            bfh.bfReserved2 = 0;
            bfh.bfOffBits = Marshal.SizeOf(bih) + Marshal.SizeOf(bfh);
            if (bih.bmiHeader.biBitCount < 8)
            {
                //There is a palette between header and pixel data
                bfh.bfOffBits += bih.bmiHeader.biClrUsed * Avi.RGBQUAD_SIZE; //Avi.PALETTE_SIZE;
            }

            //write a bitmap stream
            BinaryWriter bw = new BinaryWriter(new MemoryStream());

            //write header
            bw.Write(bfh.bfType);
            bw.Write(bfh.bfSize);
            bw.Write(bfh.bfReserved1);
            bw.Write(bfh.bfReserved2);
            bw.Write(bfh.bfOffBits);
            //write bitmap info
            bw.Write(bitmapInfo);
            //write bitmap data
            bw.Write(bitmapData);

            Bitmap bmp = (Bitmap)Image.FromStream(bw.BaseStream);
            Bitmap saveableBitmap = new Bitmap(bmp.Width, bmp.Height);
            Graphics g = Graphics.FromImage(saveableBitmap);
            g.DrawImage(bmp, 0, 0);
            g.Dispose();
            bmp.Dispose();

            bw.Close();
            return saveableBitmap;
        }

        /// <summary>Free ressources that have been used by GetFrameOpen</summary>
        public void GetFrameClose()
        {
            if (getFrameObject != 0)
            {
                Avi.AVIStreamGetFrameClose(getFrameObject);
                getFrameObject = 0;
            }
        }

        /// <summary>Copy the stream into a new file</summary>
        /// <param name="fileName">Name of the new file</param>
        public override void ExportStream(String fileName)
        {
            Avi.AVICOMPRESSOPTIONS_CLASS opts = new Avi.AVICOMPRESSOPTIONS_CLASS();
            opts.fccType = (uint)Avi.streamtypeVIDEO;
            opts.lpParms = IntPtr.Zero;
            opts.lpFormat = IntPtr.Zero;
            IntPtr streamPointer = StreamPointer;
            Avi.AVISaveOptions(IntPtr.Zero, Avi.ICMF_CHOOSE_KEYFRAME | Avi.ICMF_CHOOSE_DATARATE, 1, ref streamPointer, ref opts);
            Avi.AVISaveOptionsFree(1, ref opts);

            Avi.AVISaveV(fileName, 0, 0, 1, ref aviStream, ref opts);
        }
    }
}
