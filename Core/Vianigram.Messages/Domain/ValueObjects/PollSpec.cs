// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Messages.Domain.ValueObjects
{
    /// <summary>
    /// Immutable specification for a poll about to be sent. Carries the
    /// question text, 2-10 answer options, and quiz/anonymity flags that map
    /// onto Telegram's <c>inputMediaPoll</c> wire shape.
    /// </summary>
    public sealed class PollSpec
    {
        public PollSpec(string question, IList<string> options, bool isAnonymous, bool multipleAnswers, bool isQuiz, int correctIndex)
        {
            if (question == null) throw new ArgumentNullException("question");
            if (options == null) throw new ArgumentNullException("options");
            if (options.Count < 2 || options.Count > 10)
                throw new ArgumentOutOfRangeException("options", "poll must have between 2 and 10 options");
            if (isQuiz)
            {
                if (correctIndex < 0 || correctIndex >= options.Count)
                    throw new ArgumentOutOfRangeException("correctIndex", "correctIndex out of range for quiz");
                if (multipleAnswers)
                    throw new ArgumentException("quiz cannot allow multiple answers", "multipleAnswers");
            }
            else
            {
                correctIndex = -1;
            }

            var copy = new string[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == null) throw new ArgumentException("option text cannot be null", "options");
                copy[i] = options[i];
            }

            Question = question;
            Options = copy;
            IsAnonymous = isAnonymous;
            MultipleAnswers = multipleAnswers;
            IsQuiz = isQuiz;
            CorrectIndex = correctIndex;
        }

        public string Question { get; private set; }
        public IList<string> Options { get; private set; }
        public bool IsAnonymous { get; private set; }
        public bool MultipleAnswers { get; private set; }
        public bool IsQuiz { get; private set; }
        public int CorrectIndex { get; private set; }
    }
}
