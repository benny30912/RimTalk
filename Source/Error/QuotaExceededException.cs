<<<<<<< HEAD
﻿using RimTalk.Client;
=======
using RimTalk.Client;
>>>>>>> upstream/main

namespace RimTalk.Error;

public class QuotaExceededException : AIRequestException
{
    public QuotaExceededException(string message, Payload payload = null) : base(message, payload)
    {
    }
}
