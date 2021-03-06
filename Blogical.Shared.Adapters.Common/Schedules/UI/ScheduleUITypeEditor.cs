using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Xml;


namespace Blogical.Shared.Adapters.Common.Schedules.UI
{
    /// <summary>
    /// Implements a user interface for setting schedule parameters
    /// within a visual designer.
    /// </summary>
    [ComVisible(false)]
    public class ScheduleUITypeEditor : UITypeEditor, IDisposable 
	{
		private IWindowsFormsEditorService _service;
		private ScheduleDialog _dialog;

        /// <summary>
        /// Returns Modal Type
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
		public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) 
		{
			if (context?.Instance != null) 
			{
				return UITypeEditorEditStyle.Modal;
			}
			return base.GetEditStyle(context);
		}
        /// <summary>
        /// Called when editor is closed
        /// </summary>
        /// <param name="context"></param>
        /// <param name="provider"></param>
        /// <param name="value"></param>
        /// <returns></returns>
		public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value) 
		{
			if (context?.Instance != null && null != provider) 
			{
				_service = (IWindowsFormsEditorService) provider.GetService(typeof(IWindowsFormsEditorService));
				if (null != _service) 
				{
					_dialog = new ScheduleDialog();
					if (value != null)
						_dialog.ConfigXml.LoadXml((string)value);
					if (_service.ShowDialog(_dialog) == DialogResult.OK)
					{
						value = _dialog.ConfigXml.OuterXml;
					}
				}
			}
			return value;
		}
		private void Exit(object sender, EventArgs e)
		{
		    _service?.CloseDropDown();
		}

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _dialog.Dispose();
                }

                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
    /// <summary>
    /// ...
    /// </summary>
    [ComVisible(false)]
	public class ScheduleConverter : StringConverter 
	{
        /// <summary>
        /// Return false
        /// </summary>
        /// <param name="context"></param>
        /// <param name="sourceType"></param>
        /// <returns></returns>
		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
		{
			return false;
		}
        /// <summary>
        /// Return true
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
		public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
		{
			return true;
		} 
        /// <summary>
        /// ...
        /// </summary>
        /// <param name="context"></param>
        /// <param name="culture"></param>
        /// <param name="value"></param>
        /// <param name="destinationType"></param>
        /// <returns></returns>
		public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
		{
			if (typeof(string) == destinationType && value is string)
			{
				if ((string)value == string.Empty)
					return string.Empty;			   
				XmlDocument configXml = new XmlDocument();
				configXml.LoadXml((string)value);
				return configXml.DocumentElement.GetAttribute("type") + " Schedule";
			}
			return base.ConvertTo(context, culture, value, destinationType);
		}
	}


} 
