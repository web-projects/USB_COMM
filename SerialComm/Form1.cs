using System;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace SerialComm
{
    public partial class Form1 : Form
    {
        bool initial = false;
        bool open = false;
        string baud;
        SerialComm comm;

        delegate void SetTextCallback(string text);
        internal delegate void SerialDataReceivedEventHandlerDelegate(object sender, SerialDataReceivedEventArgs e);

        public Form1()
        {
            InitializeComponent();
        }

        private void Load_Click(object sender, EventArgs e)
        {
            comm = new SerialComm();

            if (comm != null)
            {
                string[] ports = comm.Initialize(new SerialDataReceivedEventHandler(Port_DataReceived));

                if (ports.Count() > 0)
                {
                    comboBox1.Items.Clear();
                    Array.Sort(ports);
                    foreach (var port in ports)
                    {
                        comboBox1.Items.Add(port);
                    }
                    comboBox1.SelectedIndex = 0;
                    comboBox1.Enabled = true;
                    comboBox2.SelectedIndex = 2;
                    comboBox2.Enabled = true;
                }

            }
        }

        private void UnLoad_Click(object sender, EventArgs e)
        {
            if (comm != null)
            {
                if (open)
                {
                    comm.Close();
                    open = false;
                    MessagetextBox1.Enabled = false;
                    SendBtn.Enabled = false;
                    LoadBtn.Text = "Load";
                    LoadBtn.Click -= new System.EventHandler(this.UnLoad_Click);
                    LoadBtn.Click += new System.EventHandler(this.Load_Click);
                    comboBox1.SelectedIndex = 0;
                    initial = false;
                    comboBox2.Enabled = true;
                }
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (initial)
            {
                if (!comboBox1.SelectedItem.Equals(""))
                {
                    if (comm != null)
                    {
                        if (open)
                        {
                            comm.Close();
                            open = false;
                            MessagetextBox1.Enabled = false;
                            SendBtn.Enabled = false;
                        }

                        open = comm.Open(comboBox1.SelectedItem.ToString(), baud, "8", "1", "None", "0");

                        if (open)
                        {
                            MessagetextBox1.Text = "02hPUR.10.99._000000000004.634._4761739001010010FFFFF.0808.123456..03h";
                            MessagetextBox1.Enabled = true;
                            SendBtn.Enabled = true;
                            comboBox2.Enabled = false;
                            LoadBtn.Text = "Unload";
                            LoadBtn.Click -= new System.EventHandler(this.Load_Click);
                            LoadBtn.Click += new System.EventHandler(this.UnLoad_Click);
                        }
                    }
                }
            }
            else
            {
                initial = true;
            }
        }

        private void SendBtn_Click(object sender, EventArgs e)
        {
            if (open)
            {
                comm.Write(MessagetextBox1.Text);
            }
        }

        private void SetText(string text)
        {
            this.ReceivedtextBox1.Text += text;
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string buffer = comm.Read();
            if (buffer != null)
            {
                this.BeginInvoke(new SetTextCallback(SetText), new object[] { buffer });
            }
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            baud = comboBox2.SelectedItem.ToString();
        }
    }
}
