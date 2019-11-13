using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BilibiliEvolved.Build.Watcher
{
  public class VueTypeScriptWatcher : Watcher
  {
    public VueTypeScriptWatcher() : base($".ts-output")
    {
      GenericFilter = "*.vue.js";
    }
    private Dictionary<string, TaskCompletionSource<string>> waitRequests = new Dictionary<string, TaskCompletionSource<string>>();
    public Task<string> WaitForBuild(string path) {
      var tcs = new TaskCompletionSource<string>();
      waitRequests[path] = tcs;
      return tcs.Task;
    }
    protected override void OnFileChanged(FileSystemEventArgs e)
    {
      var originalFilename = Path.ChangeExtension(e.FullPath, ".ts").Replace(WatcherPath, "src");
      if (File.Exists(originalFilename)) {
        File.Delete(originalFilename);
      }
      if (waitRequests.ContainsKey(e.FullPath)) {
        waitRequests[e.FullPath].SetResult(File.ReadAllText(e.FullPath));
        waitRequests.Remove(e.FullPath);
      }
    }
  }
  public class VueWatcher : Watcher
  {
    private VueTypeScriptWatcher tsWatcher = new VueTypeScriptWatcher();
    public VueWatcher() : base($"src{Path.DirectorySeparatorChar}")
    {
      GenericFilter = "*.vue";
      tsWatcher.Start(builder);
    }
    public override void Stop() {
      base.Stop();
      tsWatcher.Stop();
    }
    protected override void OnFileChanged(FileSystemEventArgs e)
    {
      builder.WriteInfo($"[Vue] {e.Name} changed.");
      var source = File.ReadAllText(e.FullPath);
      var vueFile = new VueFile(source);
      var compiledText = new StringBuilder("");
      BuildTemplate(e.FullPath, vueFile, compiledText);
      BuildStyle(e.FullPath, vueFile, compiledText);
      BuildScript(e.FullPath, vueFile, compiledText);
      var minFile = $"min{Path.DirectorySeparatorChar + Path.GetFileName(e.FullPath)}.min.js";
      var minifier = new JavascriptMinifier();
      File.WriteAllText(minFile, minifier.Minify(compiledText.ToString()));
      cache.AddCache(e.FullPath);
      cache.SaveCache();
    }
    private void BuildTemplate(string path, VueFile vueFile, StringBuilder compiledText)
    {
      if (vueFile.Tamplate is null)
      {
        throw new VueBuildException($"{path}: Missing <tamplate>");
      }
      else
      {
        var uglifyHtml = new UglifyHtml();
        if (vueFile.TamplateLang == "html")
        {
          compiledText.Append($"const template = /*html*/`{uglifyHtml.Run(vueFile.Tamplate)}`;");
        }
        else
        {
          throw new VueBuildException($"{path}: Unsupported <template> lang '{vueFile.TamplateLang}'");
        }
      }
    }
    private void BuildStyle(string path, VueFile vueFile, StringBuilder compiledText)
    {
      if (vueFile.Style != null)
      {
        var styleID = Path.ChangeExtension(Path.GetFileName(path), null).Replace(".", "-") + "-style";
        var uglifyCss = new UglifyCss();
        if (vueFile.StyleLang == "css")
        {
          compiledText.Append($"resources.applyStyleFromText(`{uglifyCss.Run(vueFile.Style)}`,'{styleID}');");
        }
        else if (vueFile.StyleLang == "scss")
        {
          var sass = new SassSingleCompiler();
          var css = uglifyCss.Run(sass.Run(vueFile.Style).Replace("@charset \"UTF-8\";", ""));
          compiledText.Append($"resources.applyStyleFromText(`{css}`,'{styleID}');");
        }
        else
        {
          throw new VueBuildException($"{path}: Unsupported <style> lang '{vueFile.StyleLang}'");
        }
      }
    }
    private void BuildScript(string path, VueFile vueFile, StringBuilder compiledText)
    {
      if (vueFile.Script is null)
      {
        throw new VueBuildException($"{path}: Missing <script>");
      }
      else
      {
        if (vueFile.ScriptLang == "js" || vueFile.ScriptLang == "javascript")
        {
          var script = vueFile.Script.Replace("export default ", "return {export:Object.assign({template},").Trim().TrimEnd(';');
          compiledText.Append($"{script})}}");
        }
        else if (vueFile.ScriptLang == "ts" || vueFile.ScriptLang == "typescript")
        {
          var tsFile = path + ".ts";
          File.WriteAllText(tsFile, vueFile.Script);
          var task = tsWatcher.WaitForBuild(path);
          var script = task.Result.Replace("export default ", "return {export:Object.assign({template},").Trim().TrimEnd(';');
          compiledText.Append($"{script})}}");
        }
        else
        {
          throw new VueBuildException($"{path}: Unsupported <script> lang '{vueFile.ScriptLang}'");
        }
      }
    }

  }
}
