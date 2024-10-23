using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cognex.VisionPro;
using DALSA.SaperaLT.SapClassBasic;
namespace Sapera_Demo
{
    public partial class Form_Main : Form
    {
        ClsSapera camera;
        int i = 0;
        public Form_Main()
        {
            InitializeComponent();
            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            camera.Snap();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            camera.Grab();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            camera.Freeze();
            
        }
        private void grabcallback(ICogImage cog, SapBuffer buffer)
        {
            this.cogDisplay1.AutoFit = true;
            this.cogDisplay1.Image = cog;
            buffer.Save("C:\\Users\\1\\Desktop\\"+i.ToString()+".b,p", "-format bmp");
            i++;
        }

        private void Form_Main_Load(object sender, EventArgs e)
        {
            this.cogDisplayStatusBarV21.Display = this.cogDisplay1;
            camera = new ClsSapera();
            camera.Connect("C:\\Users\\1\\Desktop\\JY\\SaperaGrabDemo\\GrabDemo");
            camera.GrabCallBack += grabcallback;
        }

        private void Form_Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            camera.Close();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            //ccf파일을 변경할 땐 카메라를 종료시킨후 다시 커넥트.
            camera.Close();
            camera.Connect("C:\\Users\\1\\Desktop\\JY\\Sapera_Demo\\Sapera_Demo\\testCCF");
        }
    }
}
