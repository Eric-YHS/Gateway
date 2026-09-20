<%@ Page Language="C#" %>
<script runat="server">
  protected void Page_Load(object sender, EventArgs e)
  {
    var mode = (Request.QueryString["mode"] ?? "show").Trim().ToLowerInvariant();
    var code = (Request.QueryString["code"] ?? "").Trim();
    var delayText = (Request.QueryString["delayMs"] ?? "").Trim();
    int delay;
    if (int.TryParse(delayText, out delay) && delay > 0 && delay <= 10000) System.Threading.Thread.Sleep(delay);
    Response.Clear();
    Response.ContentType = "application/json; charset=utf-8";
    var worker = System.Configuration.ConfigurationManager.AppSettings["WorkerName"] ?? "worker2";
    var sessionId = Session == null ? "" : Session.SessionID;
    var captcha = Session == null ? null : Convert.ToString(Session["Captcha"]);
    var createdBy = Session == null ? null : Convert.ToString(Session["CreatedBy"]);
    if (mode == "captcha")
    {
      captcha = Request.QueryString["value"] ?? "2468";
      Session["Captcha"] = captcha;
      Session["CreatedBy"] = worker;
      captcha = Convert.ToString(Session["Captcha"]);
      createdBy = Convert.ToString(Session["CreatedBy"]);
    }
    var ok = mode == "login" && !String.IsNullOrWhiteSpace(captcha) && String.Equals(captcha, code, StringComparison.Ordinal);
    if (mode == "login") Response.StatusCode = ok ? 200 : 400;
    var json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
      ok = mode == "captcha" || mode == "show" ? true : ok,
      mode = mode,
      worker = worker,
      process = System.Diagnostics.Process.GetCurrentProcess().Id,
      appDomain = AppDomain.CurrentDomain.Id,
      sessionId = sessionId,
      captchaPresent = !String.IsNullOrWhiteSpace(captcha),
      captchaValue = captcha ?? "",
      createdBy = createdBy ?? "",
      supplied = code
    });
    Response.Write(json);
    Context.ApplicationInstance.CompleteRequest();
  }
</script>
