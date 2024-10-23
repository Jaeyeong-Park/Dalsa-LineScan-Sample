using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using DALSA.SaperaLT.SapClassBasic;
using Cognex.VisionPro;
namespace Sapera_Demo
{

    class ClsSapera
    {
        public delegate void GrabCallBackEventHandler(ICogImage cog, SapBuffer currentBuffer);
        #region Variable
        SapAcquisition Acq = null;
        SapAcqDevice AcqDevice = null;
        SapFeature Feature = null;
        SapBuffer Buffers = null;
        SapTransfer Xfer = null;
        MyAcquisitionParams acqParams;
        Boolean isNotSupported = false, acquisitionCreated = true, acqDeviceCreated = true;
        
        // Static Variables
        const int GAMMA_FACTOR = 10000;
        const int MAX_CONFIG_FILES = 36;       // 10 numbers + 26 letters

        public enum BUFFER_LAYOUT { Profile, Range };

        static public int PROFILE_WINDOW_HEIGHT = 1024;
        static public int CLUSTERS_DIVISOR = ushort.MaxValue / (PROFILE_WINDOW_HEIGHT - 1) + 1;  //To clusterize the range from 0-65535 to 0-ProfileWindowHeight

        List<string> serverNameList;
        Dictionary<string, List<string>> AcqDevicesName;
        int serverCount;
        int GrabberIndex = 0;
        string ccfPath;
        string[] ccffiles;
        SapLocation loc;
        SapLocation loc2;
        IntPtr ptr;
        byte[] buffer;
        CogImageConverter convert;
        #endregion

        #region Constructor
        public ClsSapera()
        {
            
            serverCount = SapManager.GetServerCount();
            serverNameList = new List<string>();
            AcqDevicesName = new Dictionary<string, List<string>>();
        }
        #endregion

        #region Property
        #endregion

        #region Event
        public event GrabCallBackEventHandler GrabCallBack;
        private void GrabCallBackFunc(ICogImage cog, SapBuffer currentBuffer)
        {
            GrabCallBack?.Invoke(cog, currentBuffer);
        }
        private void xfer_XferNotify(object sender, SapXferNotifyEventArgs args)
        {
            if (!args.Trash)
            {
                Buffers.GetAddress(out ptr);
                if (buffer == null) { buffer = new byte[Buffers.Width * Buffers.Height]; }
                if (convert == null) { convert = new CogImageConverter(Buffers.Width, Buffers.Height, false); }
                Marshal.Copy(ptr, buffer, 0, buffer.Length);
                convert.UpdateCogimage(buffer);
                GrabCallBackFunc(convert.cogimage,Buffers);
                
            }
        }

        #endregion

        #region Method
        public bool Connect(string ccffilepath)
        {
            acqParams = new MyAcquisitionParams();
            if (!scanBoard()) { return false; }
            SelectBoard(1);
            if (!ScanAcquisitionOption()) { return false; }
            SelectAcquisitionOption(0);
            if (!ScanCCF(ccffilepath)) { return false; }
            SelectCCF(0);
            loc = new SapLocation(acqParams.ServerName, acqParams.ResourceIndex);
            if(SapManager.GetResourceCount(acqParams.ServerName, SapManager.ResourceType.Acq) > 0)
            {
                Acq = new SapAcquisition(loc, acqParams.ConfigFileName);
                Buffers = new SapBuffer(1, Acq, SapBuffer.MemoryType.ScatterGather);
                Xfer = new SapAcqToBuf(Acq, Buffers);
                if (!Acq.Create()) { acquisitionCreated = false; }
            }
            SelectCamera();
            loc2 = new SapLocation(acqParams.ServerName, acqParams.ResourceIndex);
            AcqDevice = new SapAcqDevice(loc2, false);
            Feature = new SapFeature(loc2);
            Feature.Create();

            if (!AcqDevice.Create()) { acqDeviceCreated = false; }

            if(!acquisitionCreated || !acqDeviceCreated)
            {
                DestroysObjects(Acq, Feature, AcqDevice, Buffers, Xfer);
            }
            if (!AcqDevice.IsFeatureAvailable("DeviceModelName")) { isNotSupported = true; }
            if (isNotSupported) { DestroysObjects(Acq, Feature, AcqDevice, Buffers, Xfer); }
            Xfer.Pairs[0].EventType = SapXferPair.XferEventType.EndOfFrame;
            Xfer.XferNotify += new SapXferNotifyHandler(xfer_XferNotify);
            if (!Buffers.Create()) { return false; }
            if (!Xfer.Create()) { return false; }
            return true;
        }
        public void Grab()
        {
            Xfer.Grab();
        }

        public void Freeze()
        {
            Xfer.Freeze();
            if (!Xfer.Wait(5000))
            {
                Xfer.Abort();
            }

        }

        public void Snap()
        {
            Xfer.Snap();
            if (!Xfer.Wait(5000))
            {
                Xfer.Abort();
            }
            
        }
        public void Close()
        {
            DestroysObjects(Acq, Feature, AcqDevice, Buffers, Xfer);
            if(loc != null) { loc.Dispose(); }
            if(loc2 != null) { loc2.Dispose(); }
            if (serverNameList != null)
            {
                serverNameList.Clear();
                serverNameList = null;
            }
            if (AcqDevicesName != null)
            {
                AcqDevicesName.Clear();
                AcqDevicesName = null;
            }
            if (ccffiles != null)
            {
                ccffiles = null;
            }
            if (convert != null)
            {
                convert.Dispose();
                convert = null;
            }
            if(acqParams != null)
            {
                acqParams = null;
            }
            if(buffer != null)
            {
                Array.Clear(buffer, 0, buffer.Length);
                buffer = null;
            }
        }
        private bool scanBoard()
        {
            if (serverCount == 0) { return false; }
            bool serverFound = false;
            for(int i = 0; i < serverCount; i++)
            {
                if(SapManager.GetResourceCount(i,SapManager.ResourceType.Acq) != 0)
                {
                    GrabberIndex++;
                    string servername = SapManager.GetServerName(i);
                    if(serverNameList == null) { serverNameList = new List<string>(); }
                    serverNameList.Add(servername);
                    serverFound = true;
                }
            }
            if (!serverFound) { return false; }
            return true;
        }

        private void SelectBoard(int serverindex)
        {
            acqParams.ServerName = SapManager.GetServerName(serverindex);
        }

        private bool ScanAcquisitionOption()
        {
            int deviceCount = SapManager.GetResourceCount(acqParams.ServerName, SapManager.ResourceType.Acq);
            List<string> deviceNameList = new List<string>();
            for(int i = 0; i < deviceCount; i++)
            {
                string deviceName = SapManager.GetResourceName(acqParams.ServerName, SapManager.ResourceType.Acq, i);
                deviceNameList.Add(deviceName);
            }
            if(AcqDevicesName == null) { AcqDevicesName = new Dictionary<string, List<string>>(); }
            AcqDevicesName.Add(acqParams.ServerName, deviceNameList);
            return true;
        }

        private void SelectAcquisitionOption(int resourceindex)
        {
            acqParams.ResourceIndex = resourceindex;
            
        }
        
        private bool ScanCCF(string path)
        {
            ccfPath = path;
            if (!Directory.Exists(path)) { return false; }
            ccffiles = Directory.GetFiles(path, "*.ccf");
            if(ccffiles.Length == 0) { return false; }

            return true;
        }

        private void SelectCCF(int index)
        {
            acqParams.ConfigFileName = ccffiles[index];
        }

        private bool SelectCCF(string filename)
        {
            if (!File.Exists(filename)) { return false; }
            acqParams.ConfigFileName = filename;
            return true;
        }

        private bool SelectCamera()
        {
            bool cameraFound = false;
            for(int i = 0; i < serverCount; i++)
            {
                if (SapManager.GetResourceCount(i, SapManager.ResourceType.AcqDevice) != 0)
                {
                    acqParams.ServerName = SapManager.GetServerName(i);
                    acqParams.ResourceIndex = 0;
                    cameraFound = true;
                }
            }
            return cameraFound;
        }
        private void DestroysObjects(SapAcquisition acq, SapFeature feature, SapAcqDevice camera, SapBuffer buf, SapTransfer xfer)
        {
            if (xfer != null)
            {
                xfer.Destroy();
                xfer.Dispose();
            }

            if (buf != null)
            {
                buf.Destroy();
                buf.Dispose();
            }

            if (acq != null)
            {
                acq.Destroy();
                acq.Dispose();
            }

            if (feature != null)
            {
                feature.Destroy();
                feature.Dispose();
            }
            if (camera != null)
            {
                camera.Destroy();
                camera.Dispose();
            }

        }
        #endregion

    }
    #region Class - AcquisitionParams
    class MyAcquisitionParams
    {
        public MyAcquisitionParams()
        {
            m_ServerName = "";
            m_ResourceIndex = 0;
            m_ConfigFileName = "";
        }

        public MyAcquisitionParams(string ServerName, int ResourceIndex)
        {
            m_ServerName = ServerName;
            m_ResourceIndex = ResourceIndex;
            m_ConfigFileName = "";
        }

        public MyAcquisitionParams(string ServerName, int ResourceIndex, string ConfigFileName)
        {
            m_ServerName = ServerName;
            m_ResourceIndex = ResourceIndex;
            m_ConfigFileName = ConfigFileName;
        }

        public string ConfigFileName
        {
            get { return m_ConfigFileName; }
            set { m_ConfigFileName = value; }
        }

        public string ServerName
        {
            get { return m_ServerName; }
            set { m_ServerName = value; }
        }

        public int ResourceIndex
        {
            get { return m_ResourceIndex; }
            set { m_ResourceIndex = value; }
        }

        protected string m_ServerName;
        protected int m_ResourceIndex;
        protected string m_ConfigFileName;
    }
    #endregion

    #region cognexConvert
    class CogImageConverter : IDisposable
    {
        #region Variable
        CogImage8Root _Root0;
        CogImage8Root _Root1;
        CogImage8Root _Root2;
        CogImage8Grey greyimg;
        CogImage24PlanarColor colorimg;

        GCHandle _Handle;
        #endregion

        #region Construcotr
        public CogImageConverter(int width, int height, bool color)
        {
            Width = width;
            Height = height;
            BitsPerPixel = color ? 24 : 8;
            Stride = color ? width * 4 : width;
            _Root0 = new CogImage8Root();
            if (BitsPerPixel == 8)
            {
                greyimg = new CogImage8Grey();
            }
            else if (BitsPerPixel == 24)
            {
                _Root1 = new CogImage8Root();
                _Root2 = new CogImage8Root();
                colorimg = new CogImage24PlanarColor();
            }

        }
        #endregion

        #region Proterty
        public int Stride { get; private set; }
        public IntPtr Scan0 { get; private set; }
        public int Height { get; private set; }
        public int Width { get; private set; }
        public int BitsPerPixel { get; private set; }
        public ICogImage8RootBuffer Root0 { get { return _Root0; } }
        public ICogImage8RootBuffer Root1 { get { return _Root1; } }
        public ICogImage8RootBuffer Root2 { get { return _Root2; } }

        public ICogImage cogimage { get; private set; }
        #endregion

        #region Method
        public void UpdateCogimage(byte[] buffer)
        {
            if (_Handle.IsAllocated) { _Handle.Free(); }
            _Handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            Scan0 = _Handle.AddrOfPinnedObject();
            _Root0.Initialize(Width, Height, Scan0, Width, this);
            if (BitsPerPixel == 8)
            {
                greyimg.SetRoot(Root0);
                cogimage = greyimg;
            }
            else if (BitsPerPixel == 24)
            {
                IntPtr ptr1 = new IntPtr(Scan0.ToInt64() + (Width * Height));
                _Root1.Initialize(Width, Height, ptr1, Stride, this);
                IntPtr ptr2 = new IntPtr(Scan0.ToInt64() + (Width * Height) * 2);
                _Root2.Initialize(Width, Height, ptr1, Stride, this);
                colorimg.SetRoots(Root0, Root1, Root2);
                cogimage = colorimg;
            }
        }
        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // 중복 호출을 검색하려면

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_Handle.IsAllocated)
                    {
                        _Handle.Free();
                    }
                }

                // TODO: 관리되지 않는 리소스(관리되지 않는 개체)를 해제하고 아래의 종료자를 재정의합니다.
                // TODO: 큰 필드를 null로 설정합니다.

                disposedValue = true;
            }
        }

        // TODO: 위의 Dispose(bool disposing)에 관리되지 않는 리소스를 해제하는 코드가 포함되어 있는 경우에만 종료자를 재정의합니다.
        // ~CogImage8PixelMemory()
        // {
        //   // 이 코드를 변경하지 마세요. 위의 Dispose(bool disposing)에 정리 코드를 입력하세요.
        //   Dispose(false);
        // }

        // 삭제 가능한 패턴을 올바르게 구현하기 위해 추가된 코드입니다.
        public void Dispose()
        {
            // 이 코드를 변경하지 마세요. 위의 Dispose(bool disposing)에 정리 코드를 입력하세요.
            Dispose(true);
            // TODO: 위의 종료자가 재정의된 경우 다음 코드 줄의 주석 처리를 제거합니다.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
    #endregion
}
