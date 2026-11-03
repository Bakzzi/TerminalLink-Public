namespace TerminalLink;

// Delete only the entries shown when the user confirmed. Later uploads are not touched.
internal static class FileBoxClear {
 internal static (int removed,int failed) Remove(SharedFiles store,IEnumerable<SharedFile> selected){
  int removed=0,failed=0;
  foreach(var item in selected){
   try{if(store.Find(item.id)!=item){failed++;continue;}store.Delete(item.id);removed++;}
   catch(IOException){failed++;}catch(UnauthorizedAccessException){failed++;}
  }
  return (removed,failed);
 }
}
